# Deployment Profiles and Server Artifact — Implementation Plan

**Goal:** Replace the scattered desktop/headless switches with three explicit build profiles and ship
`Server` as one production image containing the static web UI and the Identity/Postgres API.

**Profiles:**

| Profile | Auth/config | UI host | Process host | Data |
|---|---|---|---|---|
| `Server` (default) | Identity + deployment config | ASP.NET static files | Kestrel | PostgreSQL |
| `StandaloneContainer` | Local user + settings UI | ASP.NET static files | Kestrel | SQLite volume |
| `Desktop` | Local user + settings UI | ASP.NET static files | Photino + Kestrel | OS app-data SQLite |

**Depends on:** nothing.  
**Unblocks:** `2026-08-04-azure-bicep-migrations-release.md` and
`2026-08-04-durable-turns-resumable-sse.md`.

## Constraints

- One profile selection must drive both the .NET and Next builds.
- `Server` is the safe default when no MSBuild property is supplied.
- Production release profiles bundle a static export; only local development runs Next separately.
- Only `Desktop` compiles or ships Photino.
- `StandaloneContainer` keeps the current unauthenticated single-user behavior.
- `Server` keeps Identity cookies, antiforgery, PostgreSQL support and Data Protection persistence.
- Do not add a build framework or a second frontend container.

## Task 1: Define and validate the profiles

**Files:**

- Modify: `wretched-whispers-server/WretchedWhispers.Api/WretchedWhispers.Api.csproj`
- Create: `wretched-whispers-server/WretchedWhispers.Api/Deployment/DeploymentProfile.cs`
- Test: `wretched-whispers-server/WretchedWhispers.Tests/Deployment/DeploymentProfileTests.cs`

- [ ] Add the `DeploymentProfile` MSBuild property with allowed values `Server`,
      `StandaloneContainer`, and `Desktop`; default it to `Server`.
- [ ] Fail the build with a clear error for any other value.
- [ ] Define exactly one compile constant per profile. Keep the preprocessor mapping in
      `DeploymentProfile.cs`; expose ordinary predicates such as `UsesIdentity`, `UsesLocalAuth`,
      `UsesSettings`, and `OpensDesktopShell` to the rest of the application.
- [ ] Include `Desktop/**` and `Photino.NET` only for `Desktop`.
- [ ] Unit-test the pure profile-to-capabilities mapping.
- [ ] Verify all profiles compile:

```bash
rtk dotnet build wretched-whispers-server/WretchedWhispers.Api/WretchedWhispers.Api.csproj -p:DeploymentProfile=Server
rtk dotnet build wretched-whispers-server/WretchedWhispers.Api/WretchedWhispers.Api.csproj -p:DeploymentProfile=StandaloneContainer
rtk dotnet build wretched-whispers-server/WretchedWhispers.Api/WretchedWhispers.Api.csproj -p:DeploymentProfile=Desktop
```

## Task 2: Separate standalone hosting from the desktop shell

**Files:**

- Create: `wretched-whispers-server/WretchedWhispers.Api/Deployment/StandaloneHost.cs`
- Create: `wretched-whispers-server/WretchedWhispers.Api/Desktop/DesktopShell.cs`
- Remove: `wretched-whispers-server/WretchedWhispers.Api/Desktop/DesktopHost.cs`
- Modify: `wretched-whispers-server/WretchedWhispers.Api/Program.cs`

- [ ] Move writable data paths, `settings.json` loading and standalone LLM defaults into
      `StandaloneHost`; it must not reference Photino.
- [ ] Move only window construction into `DesktopShell`.
- [ ] Replace the broad `#if DESKTOP` branches in `Program.cs` with profile predicates.
- [ ] Retain a compile guard only around the call to `DesktopShell`.
- [ ] Delete `WW_HEADLESS`; `StandaloneContainer` is inherently headless and `Desktop` inherently
      opens the shell.
- [ ] Register Identity only for `Server`, and `LocalAuthHandler` only for the two standalone
      profiles.
- [ ] Map settings endpoints only for standalone profiles.
- [ ] Keep the existing `/auth`, `/sessions`, and ownership tests green in Server mode.

## Task 3: Make the frontend profile explicit

**Files:**

- Modify: `wretched-whispers-web/next.config.ts`
- Create: `wretched-whispers-web/src/lib/deployment.ts`
- Modify: frontend callers of `NEXT_PUBLIC_DESKTOP`
- Modify: `wretched-whispers-web/.env.example`
- Test: `wretched-whispers-web/src/lib/deployment.test.ts`

- [ ] Replace `NEXT_EXPORT` and `NEXT_PUBLIC_DESKTOP` with
      `NEXT_PUBLIC_DEPLOYMENT_PROFILE=Server|StandaloneContainer|Desktop`.
- [ ] Export a small validated frontend profile helper; derive `isStandalone` from it rather than
      repeating environment comparisons.
- [ ] Make `next.config.ts` use static export whenever a release profile is supplied. With no
      profile, preserve the normal local `next dev`/hosted build behavior.
- [ ] Server frontend behavior: same-origin API, login/register visible, settings UI absent.
- [ ] Standalone behavior: same-origin API, locally authenticated, settings UI present.
- [ ] Add tests for the three mappings and invalid input.

## Task 4: Serve the UI from Server production

**Files:**

- Modify: `wretched-whispers-server/WretchedWhispers.Api/Program.cs`
- Modify: `wretched-whispers-server/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs`
- Modify: frontend API/auth/settings callers
- Test: create `wretched-whispers-server/WretchedWhispers.Tests/Deployment/StaticUiTests.cs`

- [ ] Move application endpoints under `/api` before bundling: `/api/auth`, `/api/sessions`, and
      `/api/settings`. Keep `/health/*` at the root for platform probes.
- [ ] Update the frontend API wrapper once so every caller follows the new base path.
- [ ] Remove the old unprefixed endpoints in the atomic bundled release; there are no supported
      external API clients to preserve, and retaining `GET /sessions` would collide with the
      frontend `/sessions` route.
- [ ] In packaged Server builds, enable default/static files and map the SPA fallback after API
      endpoints.
- [ ] Keep CORS only for the local development topology where Next runs on another origin.
- [ ] Fail Server startup in Production when `wwwroot/index.html` is missing; do not silently expose
      an API-only deployment.
- [ ] Test `/` and a hard reload of `/sessions` serve the packaged UI, `/api/sessions` reaches the
      API, and unknown `/api/*` routes do not accidentally return `index.html`.
- [ ] Configure forwarded headers for the trusted Azure ingress so scheme and client metadata are
      correct behind TLS termination.

## Task 5: Build both OCI profiles from one Dockerfile

**Files:**

- Modify: `Dockerfile`
- Modify: `.dockerignore`
- Modify: `build-desktop.sh`
- Modify: `README.md`

- [ ] Add `ARG DEPLOYMENT_PROFILE=StandaloneContainer` to preserve the current plain
      `docker build` result.
- [ ] Pass that argument to both the Next build and `dotnet publish`.
- [ ] Copy the static export into the API publish directory for both container profiles.
- [ ] Set only profile-appropriate runtime defaults: `/data` volume for standalone, no volume and no
      settings persistence for Server.
- [ ] Update the desktop script to pass `Desktop` to both builds.
- [ ] Document exact commands for all three profiles and clearly warn that
      `StandaloneContainer` has no login.

## Task 6: Add profile verification to CI

**Files:**

- Modify: `.github/workflows/dotnet.yml`
- Modify: `.github/workflows/web.yml`
- Modify: `.github/workflows/docker.yml`

- [ ] On pull requests, compile the API in all three profiles.
- [ ] Build frontend static exports for Server and StandaloneContainer.
- [ ] Build both container profiles without pushing.
- [ ] Smoke the Server image: `/`, `/health`, registration/login, and an authenticated API request.
- [ ] Keep image publishing separate from PR verification.

## Acceptance gate

- [ ] `rtk dotnet test wretched-whispers-server/WretchedWhispers.sln --configuration Release`
- [ ] `rtk npm run lint`, `rtk npm test`, and both frontend profile builds pass.
- [ ] Server image contains no Photino native assets and starts without a writable volume.
- [ ] Standalone image retains current first-run settings and SQLite persistence.
- [ ] Desktop package opens Photino and persists under OS app-data.
- [ ] Server exposes one same-origin UI/API endpoint with working secure cookies and antiforgery.

## Deliberately deferred

- Azure infrastructure and release automation (next plan).
- Separate Next runtime, SSR, Server Actions and image optimization.
- Terraform; the Azure plan keeps resource boundaries portable for a later migration.
