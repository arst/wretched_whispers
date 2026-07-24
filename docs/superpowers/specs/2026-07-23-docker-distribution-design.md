# Docker distribution — single-image self-hosting

**Date:** 2026-07-23
**Status:** Approved design

## Context

Wretched Whispers should be playable without cloning or building: `docker run` one image, pass
config as parameters, open the browser. The desktop build (PR #28) already solved most of the
packaging problem — static-exported SPA served same-origin from `wwwroot`, BYO OpenAI key
(`Llm:Provider=openai` + `ReloadableOpenAIChatClient`), single-user `LocalAuthHandler` auth, a
first-run settings screen (`DesktopSettingsGate` + `MapDesktopSettings`) persisting to
`settings.json`. The only container-hostile piece is the Photino native window.

## Decisions (user-approved)

1. **Approach:** headless mode of the existing DESKTOP compile flavor — no third compile flavor,
   no docker-compose. One env switch turns the desktop package into a web host.
2. **Auth:** single-user, no login (desktop's `LocalAuthHandler`). Anyone who can reach the port
   plays; fine for localhost/home-LAN self-hosting.
3. **Config entry:** env vars win; without them the existing first-run browser screen asks for
   the key and persists it to the data volume.
4. **Distribution:** Dockerfile in the repo + GitHub Actions workflow publishing
   `ghcr.io/arst/wretched-whispers` (linux/amd64 + linux/arm64) on version tags.

## 1. Headless server switch (Api, DESKTOP branch)

- `WW_HEADLESS=1` (env): the `#if DESKTOP` tail of `Program.cs` skips `DesktopHost.Run` and the
  free-port dance; instead it binds `ASPNETCORE_URLS` (default `http://0.0.0.0:8080` when unset)
  and blocks on the normal web host. Everything else stays the desktop path: `LocalAuthHandler`,
  static SPA (`UseDefaultFiles`/`UseStaticFiles`/`MapFallbackToFile`), `MapDesktopSettings`,
  `/health`.
- `DesktopHost.CreateDataDir` honours `WW_DATA_DIR` when set (container: `/data`), falling back
  to the current OS app-data path. `DbPath` and `SettingsPath` derive from it unchanged, so the
  SQLite DB and `settings.json` land on the mounted volume.
- Photino stays referenced (DESKTOP compilation) but is never constructed headless — its native
  libs ride along unused (a few MB, accepted).

## 2. Env-var config with friendly names

- A mapping layer in `Program.cs` (DESKTOP branch, applied AFTER `DesktopHost.BuildConfig`'s
  in-memory layer so it wins — later configuration sources override earlier ones): when the env
  var is non-empty, map
  - `OPENAI_API_KEY` → `Llm:ApiKey`
  - `OPENAI_MODEL` → `Llm:Model` (unset keeps the existing `gpt-4o` default)
  - `OPENAI_BASE_URL` → `Llm:BaseUrl` (OpenAI-compatible gateways, e.g. OpenRouter)
- Precedence: **env var > settings.json > first-run UI**. Note the current layering quirk:
  `BuildConfig`'s `AddInMemoryCollection` is added after the default env-var provider, so plain
  `Llm__ApiKey` env vars are today overridden by `settings.json` — the new mapping layer must be
  added after `BuildConfig` to guarantee env wins for the friendly names.
- The first-run settings gate must treat an env-provided key as configured (no key screen when
  `OPENAI_API_KEY` is set). The settings screen stays reachable for model/base-url changes;
  runtime edits keep working through `ReloadableOpenAIChatClient`.
- Standard ASP.NET Core vars (`ConnectionStrings__Default`, `GameSession__*`) keep working
  as they do today; they are not re-mapped.

## 3. Dockerfile (repo root)

Multi-stage:

1. **web** — `node:22-alpine`: `npm ci` + `NEXT_EXPORT=1 NEXT_PUBLIC_DESKTOP=1
   NEXT_PUBLIC_API_URL="" npm run build` in `wretched-whispers-web` → static `out/`.
2. **api** — `mcr.microsoft.com/dotnet/sdk:10.0`: `dotnet publish
   WrtechedWhispers/WretchedWhispers.Api -c Release -p:DesktopBuild=true` (framework-dependent,
   NOT self-contained — the aspnet base image carries the runtime); copy stage 1's `out/` into
   `publish/wwwroot`.
3. **runtime** — `mcr.microsoft.com/dotnet/aspnet:10.0`:
   - `ENV WW_HEADLESS=1 WW_DATA_DIR=/data`
   - `VOLUME /data`, `EXPOSE 8080`
   - `HEALTHCHECK` hitting `http://localhost:8080/health`
   - `ENTRYPOINT ["dotnet", "WretchedWhispers.Api.dll"]`

Usage (documented in README):

```bash
docker run -p 8080:8080 -v ww-data:/data -e OPENAI_API_KEY=sk-... ghcr.io/arst/wretched-whispers
# or zero-config: omit the key and paste it in the browser on first run
docker run -p 8080:8080 -v ww-data:/data ghcr.io/arst/wretched-whispers
```

Optional vars: `OPENAI_MODEL` (default gpt-4o), `OPENAI_BASE_URL` (OpenAI-compatible gateway).

## 4. GHCR publish workflow (`.github/workflows/docker.yml`)

- Triggers: tag push `v*` + `workflow_dispatch`.
- `docker/setup-qemu-action` + `docker/setup-buildx-action` + `docker/build-push-action`;
  platforms `linux/amd64,linux/arm64`; login to GHCR with `GITHUB_TOKEN`
  (`permissions: packages: write`); tags `ghcr.io/arst/wretched-whispers:latest` and `:<tag>`.
- The existing `dotnet.yml` CI is untouched.

## 5. Tests & verification

- Unit tests for the env-mapping helper: friendly name → config key; empty/unset env leaves
  existing values; mapping layer overrides a settings.json-seeded value (precedence pin).
- Existing test suite must stay green (the headless switch is inside `#if DESKTOP`, which the
  test projects do not compile — verify nothing else regressed).
- Manual smoke (documented in the plan): `docker build`, `docker run` with `-e OPENAI_API_KEY`,
  check `/health`, SPA loads, one game turn works; second run without env vars → first-run key
  screen appears and persists to the volume; container restart keeps DB + key.

## Deliberately skipped

- TLS/HTTPS (reverse-proxy territory — document "put Caddy/Traefik in front" one-liner).
- Multi-user Identity auth in the container.
- Image slimming beyond framework-dependent publish (no trimming/AOT).
- docker-compose file, Docker Hub publishing.
