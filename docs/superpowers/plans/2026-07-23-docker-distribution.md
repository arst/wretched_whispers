# Docker Distribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Wretched Whispers as one `docker run`-able image (`ghcr.io/arst/wretched-whispers`): single-user desktop flavor headless in a container, configured via `-e OPENAI_API_KEY=...` or the existing first-run browser screen.

**Architecture:** The DESKTOP compile flavor already packages everything (static SPA in wwwroot, BYO-key OpenAI provider, no-login LocalAuthHandler, first-run settings screen). We add a runtime headless switch (`WW_HEADLESS=1`) that binds a web host instead of opening the Photino window, a `WW_DATA_DIR` override so SQLite + settings.json land on a volume, and a friendly env-var mapping (`OPENAI_API_KEY` → `Llm:ApiKey`) layered AFTER the settings.json config so env wins. A multi-stage Dockerfile and a GHCR publish workflow complete distribution.

**Tech Stack:** .NET 10 (aspnet:10.0 runtime image), Next.js static export (node:22-alpine build stage), Docker buildx multi-arch, GitHub Actions + GHCR.

**Spec:** `docs/superpowers/specs/2026-07-23-docker-distribution-design.md`

## Global Constraints

- Never use the null-forgiving operator (`!`) — validate instead.
- Prefix all shell commands with `rtk` (docker commands too: `rtk docker ...`).
- Solution path: `WrtechedWhispers/WrtechedWhispers.sln` (directory typo intentional). Api project: `WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj`.
- Desktop-only code lives under `WretchedWhispers.Api/Desktop/` and compiles ONLY with `-p:DesktopBuild=true` (csproj `Compile Remove="Desktop\**"` gate + `DESKTOP` define). The test projects compile WITHOUT it — anything needing unit tests must live outside that gate.
- Config precedence (spec-pinned): env var > settings.json > first-run UI. `DesktopHost.BuildConfig()`'s `AddInMemoryCollection` is added after the default env provider, so the friendly-name mapping MUST be added as a later `AddInMemoryCollection` layer to win.
- Friendly env vars: `OPENAI_API_KEY` → `Llm:ApiKey`, `OPENAI_MODEL` → `Llm:Model`, `OPENAI_BASE_URL` → `Llm:BaseUrl`. Empty/whitespace env values map nothing.
- Container defaults: `WW_HEADLESS=1`, `WW_DATA_DIR=/data`, port 8080, `ENTRYPOINT ["dotnet", "WretchedWhispers.Api.dll"]`.
- Work on branch `feat/docker-distribution` (create from `main` in Task 1, step 0).

---

### Task 1: `EnvConfigOverrides` mapping (testable, outside the DESKTOP gate)

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Engine/Configuration/EnvConfigOverrides.cs`
- Test: create `WrtechedWhispers/WretchedWhispers.Tests/Configuration/EnvConfigOverridesTests.cs`

**Interfaces:**
- Consumes: nothing project-specific.
- Produces: `static Dictionary<string, string?> EnvConfigOverrides.Map(Func<string, string?> getEnv)` — pure function; Task 2 wires it into `Program.cs` as `builder.Configuration.AddInMemoryCollection(EnvConfigOverrides.Map(Environment.GetEnvironmentVariable))`.

- [ ] **Step 0: Create the branch**

```bash
rtk git checkout -b feat/docker-distribution main
```

- [ ] **Step 1: Write the failing tests**

Create `EnvConfigOverridesTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using WretchedWhispers.Engine.Configuration;
using Xunit;

namespace WretchedWhispers.Tests.Configuration;

public sealed class EnvConfigOverridesTests
{
    private static string? Env(string name, params (string Name, string Value)[] vars) =>
        vars.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void Map_TranslatesFriendlyNamesToConfigKeys()
    {
        var result = EnvConfigOverrides.Map(n => Env(n,
            ("OPENAI_API_KEY", "sk-test"), ("OPENAI_MODEL", "gpt-5-mini"),
            ("OPENAI_BASE_URL", "https://openrouter.ai/api/v1")));

        Assert.Equal("sk-test", result["Llm:ApiKey"]);
        Assert.Equal("gpt-5-mini", result["Llm:Model"]);
        Assert.Equal("https://openrouter.ai/api/v1", result["Llm:BaseUrl"]);
    }

    [Fact]
    public void Map_UnsetOrBlankEnv_MapsNothing()
    {
        Assert.Empty(EnvConfigOverrides.Map(_ => null));
        Assert.Empty(EnvConfigOverrides.Map(_ => "  "));
    }

    [Fact]
    public void Map_LayeredAfterSettingsConfig_EnvWins()
    {
        // Spec precedence pin: the mapping layer is added AFTER the settings.json-seeded
        // in-memory layer (later configuration sources win), so an env key overrides a saved one.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:ApiKey"] = "sk-from-settings" })
            .AddInMemoryCollection(EnvConfigOverrides.Map(n => n == "OPENAI_API_KEY" ? "sk-from-env" : null))
            .Build();

        Assert.Equal("sk-from-env", config["Llm:ApiKey"]);
    }

    [Fact]
    public void Map_PartialEnv_LeavesOtherKeysUntouched()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                { ["Llm:ApiKey"] = "sk-from-settings", ["Llm:Model"] = "gpt-4o" })
            .AddInMemoryCollection(EnvConfigOverrides.Map(n => n == "OPENAI_MODEL" ? "gpt-5-mini" : null))
            .Build();

        Assert.Equal("sk-from-settings", config["Llm:ApiKey"]); // not clobbered by an absent env var
        Assert.Equal("gpt-5-mini", config["Llm:Model"]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~EnvConfigOverridesTests"`
Expected: compile error — `EnvConfigOverrides` not defined.

- [ ] **Step 3: Implement**

Create `EnvConfigOverrides.cs`:

```csharp
namespace WretchedWhispers.Engine.Configuration;

/// <summary>
/// Maps friendly container env vars (OPENAI_API_KEY, ...) to the Llm:* configuration keys the
/// OpenAI provider reads. Lives outside the DESKTOP compile gate so it is unit-testable; the
/// desktop/headless Program applies it as the LAST configuration layer so env vars beat the
/// settings.json values seeded by DesktopHost.BuildConfig (spec: env > settings.json > first-run UI).
/// </summary>
public static class EnvConfigOverrides
{
    private static readonly (string Env, string Key)[] Mappings =
    [
        ("OPENAI_API_KEY", "Llm:ApiKey"),
        ("OPENAI_MODEL", "Llm:Model"),
        ("OPENAI_BASE_URL", "Llm:BaseUrl"),
    ];

    public static Dictionary<string, string?> Map(Func<string, string?> getEnv)
    {
        var overrides = new Dictionary<string, string?>();
        foreach (var (env, key) in Mappings)
        {
            var value = getEnv(env);
            if (!string.IsNullOrWhiteSpace(value)) overrides[key] = value;
        }
        return overrides;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~EnvConfigOverridesTests"`
Expected: PASS (4 facts).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Engine/Configuration/EnvConfigOverrides.cs WrtechedWhispers/WretchedWhispers.Tests/Configuration/EnvConfigOverridesTests.cs
rtk git commit -m "feat(config): friendly OPENAI_* env vars map to Llm:* keys"
```

---

### Task 2: Headless switch + data-dir override (DESKTOP-gated code)

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Desktop/DesktopHost.cs` (`CreateDataDir`, add `IsHeadless`)
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Program.cs` (desktop config layer ~line 20-24; desktop tail ~line 106-131)

**Interfaces:**
- Consumes: `EnvConfigOverrides.Map` (Task 1).
- Produces: `DesktopHost.IsHeadless` (bool, true when env `WW_HEADLESS` == "1"); `WW_DATA_DIR` env override for the data dir. Task 3's Dockerfile sets `WW_HEADLESS=1` + `WW_DATA_DIR=/data`.

This code is inside the `DesktopBuild=true` gate — no unit tests compile against it. The
verification is (a) a desktop-flavor build compiles, (b) the normal suite stays green, (c)
Task 3's container smoke actually runs it.

- [ ] **Step 1: `DesktopHost` changes**

Replace `CreateDataDir` and add `IsHeadless` (keep everything else as-is):

```csharp
    /// <summary>True when running headless (container/server): WW_HEADLESS=1 skips the native window.</summary>
    public static bool IsHeadless =>
        Environment.GetEnvironmentVariable("WW_HEADLESS") == "1";

    private static string CreateDataDir()
    {
        // WW_DATA_DIR (container: /data, a mounted volume) beats the per-user OS app-data path.
        var custom = Environment.GetEnvironmentVariable("WW_DATA_DIR");
        var dir = string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WretchedWhispers")
            : custom;
        Directory.CreateDirectory(dir);
        return dir;
    }
```

- [ ] **Step 2: `Program.cs` — env-override config layer**

In the `#if DESKTOP` block near the top (currently just the `AddInMemoryCollection(DesktopHost.BuildConfig())` line), add the friendly-env layer AFTER it:

```csharp
#if DESKTOP
// Desktop: point SQLite at the writable app-data dir and select the OpenAI provider (key from
// settings.json). Applied before service registration so AddDbContext / AddGameAgent pick it up.
builder.Configuration.AddInMemoryCollection(WretchedWhispers.Api.Desktop.DesktopHost.BuildConfig());
// Container/CLI overrides: OPENAI_API_KEY / OPENAI_MODEL / OPENAI_BASE_URL. Added LAST so env vars
// beat settings.json (spec precedence: env > settings.json > first-run UI).
builder.Configuration.AddInMemoryCollection(
    WretchedWhispers.Engine.Configuration.EnvConfigOverrides.Map(Environment.GetEnvironmentVariable));
#endif
```

(`WretchedWhispers.Engine.Configuration` is already imported at the top of Program.cs as `using WretchedWhispers.Engine.Configuration;` — if so, the short name `EnvConfigOverrides.Map(...)` is fine; keep whichever matches the file.)

- [ ] **Step 3: `Program.cs` — headless tail**

Replace the desktop tail (from `var desktopUrl = ...` through `await app.StopAsync();`, keeping `GetFreePort` for the windowed path):

```csharp
if (WretchedWhispers.Api.Desktop.DesktopHost.IsHeadless)
{
    // Container/server: no native window. Honour ASPNETCORE_URLS when set; otherwise bind all
    // interfaces on 8080 (the container's EXPOSEd port). Blocks until the host stops.
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
        app.Urls.Add("http://0.0.0.0:8080");
    app.Run();
}
else
{
    var desktopUrl = $"http://127.0.0.1:{GetFreePort()}";
    app.Urls.Add(desktopUrl);
    await app.StartAsync();
    WretchedWhispers.Api.Desktop.DesktopHost.Run(desktopUrl); // blocks until the window closes
    await app.StopAsync();
}
```

- [ ] **Step 4: Verify both flavors build and the suite stays green**

Run: `rtk dotnet build WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj -c Release -p:DesktopBuild=true`
Expected: Build succeeded, 0 errors (DESKTOP flavor compiles with the new code).

Run: `rtk dotnet test WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj`
Expected: all pass (non-desktop compilation untouched).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Api/Desktop/DesktopHost.cs WrtechedWhispers/WretchedWhispers.Api/Program.cs
rtk git commit -m "feat(desktop): WW_HEADLESS server mode and WW_DATA_DIR override for containers"
```

---

### Task 3: Dockerfile + .dockerignore + local smoke test

**Files:**
- Create: `Dockerfile` (repo root)
- Create: `.dockerignore` (repo root)

**Interfaces:**
- Consumes: `WW_HEADLESS`/`WW_DATA_DIR` (Task 2), friendly env vars (Task 1), desktop static-export recipe from `build-desktop.sh` (`NEXT_EXPORT=1 NEXT_PUBLIC_DESKTOP=1 NEXT_PUBLIC_API_URL="" npm run build`).
- Produces: a locally buildable image `wretched-whispers:dev`; Task 4's workflow builds the same Dockerfile.

- [ ] **Step 1: Create `.dockerignore`**

```
**/bin/
**/obj/
**/node_modules/
**/.next/
wretched-whispers-web/out/
dist/
docs/
graphify-out/
.playwright-mcp/
.superpowers/
.planning/
.review-council/
.serena/
.claude/
.git/
.github/
*.md
logo.png
WrtechedWhispers/WretchedWhispers.Api/wretched-whispers.db
WrtechedWhispers/WretchedWhispers.Api/wwwroot/
```

- [ ] **Step 2: Create `Dockerfile`**

```dockerfile
# ---- Stage 1: static-export the SPA (same recipe as build-desktop.sh) ----
FROM node:22-alpine AS web
WORKDIR /src/web
COPY wretched-whispers-web/package.json wretched-whispers-web/package-lock.json ./
RUN npm ci
COPY wretched-whispers-web/ ./
ENV NEXT_EXPORT=1 NEXT_PUBLIC_DESKTOP=1 NEXT_PUBLIC_API_URL=""
RUN npm run build

# ---- Stage 2: publish the API in the desktop flavor (framework-dependent) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY WrtechedWhispers/ WrtechedWhispers/
RUN dotnet publish WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj \
    -c Release -p:DesktopBuild=true -o /app/publish
COPY --from=web /src/web/out/ /app/publish/wwwroot/

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl only for the container healthcheck; aspnet base image ships without it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api /app/publish/ ./
ENV WW_HEADLESS=1 WW_DATA_DIR=/data
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=15s \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "WretchedWhispers.Api.dll"]
```

- [ ] **Step 3: Build the image**

Run: `rtk docker build -t wretched-whispers:dev .`
Expected: all three stages succeed. (First build downloads base images — minutes.)

- [ ] **Step 4: Smoke test — health, SPA, env config**

```bash
rtk docker run -d --name ww-smoke -p 18080:8080 -e OPENAI_API_KEY=sk-smoke-test wretched-whispers:dev
sleep 8
rtk curl -fsS http://localhost:18080/health          # expect: "alive"
rtk curl -fsS http://localhost:18080/ | head -c 300  # expect: HTML of the SPA (doctype/next markup)
rtk curl -fsS http://localhost:18080/settings        # expect: {"provider":"openai",...,"hasKey":true}
```

Expected: `/health` → alive; `/` → SPA HTML; `/settings` → `hasKey: true` (env key reached `DesktopLlmOptions` through the override layer — the first-run gate will not show).

Then verify the zero-config path and volume persistence:

```bash
rtk docker rm -f ww-smoke
rtk docker volume create ww-smoke-data
rtk docker run -d --name ww-smoke -p 18080:8080 -v ww-smoke-data:/data wretched-whispers:dev
sleep 8
rtk curl -fsS http://localhost:18080/settings        # expect: hasKey:false (first-run gate would show)
rtk curl -fsS -X POST http://localhost:18080/settings -H 'Content-Type: application/json' \
  -d '{"apiKey":"sk-persisted","model":"gpt-4o","baseUrl":""}'   # expect: hasKey:true
rtk docker restart ww-smoke && sleep 8
rtk curl -fsS http://localhost:18080/settings        # expect: hasKey:true (settings.json survived restart)
rtk docker rm -f ww-smoke && rtk docker volume rm ww-smoke-data
```

Expected: all as annotated. (No real game turn — `sk-smoke-test` is not a live key; a live-key manual playtest is listed in the final verification.)

- [ ] **Step 5: Commit**

```bash
rtk git add Dockerfile .dockerignore
rtk git commit -m "feat(docker): multi-stage image - headless desktop flavor, /data volume, 8080"
```

---

### Task 4: GHCR publish workflow + README usage docs

**Files:**
- Create: `.github/workflows/docker.yml`
- Modify: `README.md` (add a "Run with Docker" section)

**Interfaces:**
- Consumes: `Dockerfile` (Task 3).
- Produces: `ghcr.io/arst/wretched-whispers:{latest,<tag>}` on `v*` tags.

- [ ] **Step 1: Create `.github/workflows/docker.yml`**

```yaml
name: Docker Publish

on:
  push:
    tags: ["v*"]
  workflow_dispatch:

jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - name: Checkout code
        uses: actions/checkout@v4
      - name: Set up QEMU
        uses: docker/setup-qemu-action@v3
      - name: Set up Buildx
        uses: docker/setup-buildx-action@v3
      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - name: Docker metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ghcr.io/${{ github.repository_owner }}/wretched-whispers
          tags: |
            type=semver,pattern={{version}}
            type=raw,value=latest,enable=${{ startsWith(github.ref, 'refs/tags/v') }}
      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: .
          platforms: linux/amd64,linux/arm64
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

- [ ] **Step 2: Add "Run with Docker" to `README.md`**

Append after the existing intro content:

```markdown
## Run with Docker

```bash
docker run -p 8080:8080 -v ww-data:/data -e OPENAI_API_KEY=sk-... ghcr.io/arst/wretched-whispers
```

Open http://localhost:8080 and play. Or run with no key and paste it in the browser on first run:

```bash
docker run -p 8080:8080 -v ww-data:/data ghcr.io/arst/wretched-whispers
```

| Env var | Meaning | Default |
|---|---|---|
| `OPENAI_API_KEY` | Your OpenAI-compatible API key | unset → first-run screen asks |
| `OPENAI_MODEL` | Chat model | `gpt-4o` |
| `OPENAI_BASE_URL` | OpenAI-compatible gateway (e.g. OpenRouter) | OpenAI |

Game data (SQLite + settings) lives in the `/data` volume. The container serves plain HTTP on
port 8080 — put Caddy/Traefik/nginx in front if you expose it beyond your machine.
```

- [ ] **Step 3: Validate the workflow file**

Run: `rtk npx --yes yaml-lint .github/workflows/docker.yml 2>/dev/null || python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/docker.yml')); print('yaml ok')"`
Expected: `yaml ok` (or lint pass). (The workflow itself can only be exercised on a tag push after merge.)

- [ ] **Step 4: Commit**

```bash
rtk git add .github/workflows/docker.yml README.md
rtk git commit -m "ci(docker): GHCR multi-arch publish on version tags + README usage"
```

---

## Final verification

1. `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln` — full suite (evals skip/pass as usual; no prompt changes in this plan).
2. Task 3's smoke test passes end-to-end on the locally built image.
3. Manual (user, post-merge): `docker run` with a REAL key, play one turn in the browser; tag `v0.x` and watch the publish workflow push to GHCR.

## Deliberately skipped (spec)

TLS, multi-user auth, image trimming/AOT, docker-compose, Docker Hub.
