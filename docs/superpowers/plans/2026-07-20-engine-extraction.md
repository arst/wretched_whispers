# WretchedWhispers.Engine Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the AI game-master layer from `WretchedWhispers.Api` into a new `WretchedWhispers.Engine` class library so a native client can run the game in-process.

**Architecture:** Pure mechanical refactor per `docs/superpowers/specs/2026-07-20-engine-extraction-design.md`. New dependency chain `Core ← Infrastructure ← Engine ← Api`. Files move with `git mv`, namespaces rename `WretchedWhispers.Api.*` → `WretchedWhispers.Engine.*`, LLM package refs move to Engine. Zero behavior change.

**Tech Stack:** .NET 10 (`net10.0`), Microsoft Agent Framework (`Microsoft.Agents.AI` 1.9.0), Azure/OpenAI clients, xunit.

## Global Constraints

- All work happens in `/home/arst/Projects/wretched_whispers/WrtechedWhispers/` (the `Wrteched` typo in the solution dir name is intentional — do not "fix" it) on branch `refactor/shared-engine`.
- No behavior change anywhere. No new abstractions, no renamed classes, no new tests — the existing 409-test suite is the safety net.
- Package versions are pinned (9.0.18 family for ASP.NET/EF). Do not introduce floating versions.
- Never run `WretchedWhispers.Evals` tests (they call a live LLM). Test command is always scoped to `WretchedWhispers.Tests`.
- Every task ends with `dotnet build WrtechedWhispers.sln` at zero errors.

---

### Task 1: Create the empty Engine project

**Files:**
- Create: `WretchedWhispers.Engine/WretchedWhispers.Engine.csproj`
- Modify: `WrtechedWhispers.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: nothing.
- Produces: an empty `WretchedWhispers.Engine` classlib project, in the solution, referencing Infrastructure and carrying the LLM package refs. Task 2 moves code into it.

- [ ] **Step 1: Create the project file**

Create `WretchedWhispers.Engine/WretchedWhispers.Engine.csproj` with exactly:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\WretchedWhispers.Infrastructure\WretchedWhispers.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Azure.AI.OpenAI" Version="2.9.0-beta.1" />
    <PackageReference Include="OpenAI" Version="2.10.0" />
    <PackageReference Include="Microsoft.Agents.AI" Version="1.9.0" />
    <PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.9.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.18" />
  </ItemGroup>

</Project>
```

Notes for the implementer:
- The four LLM packages are copied verbatim from `WretchedWhispers.Api.csproj` (they are REMOVED from Api in Task 2, not here — Api still compiles against them until its code moves).
- `Microsoft.Extensions.Options.ConfigurationExtensions` is new: `AgentConfiguration.cs` uses `services.Configure<T>(IConfiguration)` and `configuration.GetValue(...)`, which the Web SDK provided implicitly but a plain classlib does not.

- [ ] **Step 2: Add to solution and build**

```bash
cd /home/arst/Projects/wretched_whispers/WrtechedWhispers
dotnet sln WrtechedWhispers.sln add WretchedWhispers.Engine/WretchedWhispers.Engine.csproj
dotnet build WrtechedWhispers.sln --nologo -v q
```

Expected: build succeeds, 0 errors (7 projects now).

- [ ] **Step 3: Commit**

```bash
git add WretchedWhispers.Engine/WretchedWhispers.Engine.csproj WrtechedWhispers.sln
git commit -m "refactor(engine): add empty WretchedWhispers.Engine project

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Move the AI layer into Engine (atomic move + namespace rename)

This task must land as one commit — the solution does not compile mid-way.

**Files:**
- Move (git mv): `WretchedWhispers.Api/Services/` → `WretchedWhispers.Engine/Services/` (18 files)
- Move: `WretchedWhispers.Api/GameTools/` → `WretchedWhispers.Engine/GameTools/` (23 files incl. `Models/`)
- Move: `WretchedWhispers.Api/Prompts/` → `WretchedWhispers.Engine/Prompts/` (2 files)
- Move: `WretchedWhispers.Api/Models/GameTurnEvent.cs` → `WretchedWhispers.Engine/Models/GameTurnEvent.cs`
- Move: `WretchedWhispers.Api/Models/AzureOpenAiSettings.cs` → `WretchedWhispers.Engine/Models/AzureOpenAiSettings.cs`
- Move: `WretchedWhispers.Api/Configuration/AgentConfiguration.cs` → `WretchedWhispers.Engine/Configuration/AgentConfiguration.cs`
- Move: `WretchedWhispers.Api/Configuration/DesktopLlmOptions.cs` → `WretchedWhispers.Engine/Configuration/DesktopLlmOptions.cs`
- Modify: `WretchedWhispers.Api/WretchedWhispers.Api.csproj` (swap project ref, drop LLM packages)
- Modify: `using` lines in `WretchedWhispers.Api/{Program.cs, Endpoints/SessionEndpoints.cs, Endpoints/SettingsEndpoints.cs, Configuration/OpenTelemetryConfiguration.cs}`, `WretchedWhispers.Tests/**` (13 files), `WretchedWhispers.Evals/Harness/**` (3 files)

**Interfaces:**
- Consumes: the empty Engine project from Task 1.
- Produces: `WretchedWhispers.Engine.Services.TurnCoordinator.ExecuteTurnAsync(Guid sessionId, string playerMessage, CancellationToken ct)` returning `IAsyncEnumerable<WretchedWhispers.Engine.Models.GameTurnEvent>`, and `WretchedWhispers.Engine.Configuration.AgentConfiguration.AddGameAgent(this IServiceCollection, IConfiguration)`. Class names unchanged; only namespaces change.

- [ ] **Step 1: Move the files**

```bash
cd /home/arst/Projects/wretched_whispers/WrtechedWhispers
git mv WretchedWhispers.Api/Services WretchedWhispers.Engine/Services
git mv WretchedWhispers.Api/GameTools WretchedWhispers.Engine/GameTools
git mv WretchedWhispers.Api/Prompts WretchedWhispers.Engine/Prompts
mkdir -p WretchedWhispers.Engine/Models WretchedWhispers.Engine/Configuration
git mv WretchedWhispers.Api/Models/GameTurnEvent.cs WretchedWhispers.Engine/Models/
git mv WretchedWhispers.Api/Models/AzureOpenAiSettings.cs WretchedWhispers.Engine/Models/
git mv WretchedWhispers.Api/Configuration/AgentConfiguration.cs WretchedWhispers.Engine/Configuration/
git mv WretchedWhispers.Api/Configuration/DesktopLlmOptions.cs WretchedWhispers.Engine/Configuration/
```

- [ ] **Step 2: Rename namespaces inside Engine**

Every moved file's `namespace` and internal `using` lines flip from `Api` to `Engine`. Blanket replace is safe: it was verified that moved files reference only moved namespaces (no `ChatMessageDto`/wire DTOs).

```bash
find WretchedWhispers.Engine -name "*.cs" -exec sed -i 's/WretchedWhispers\.Api\./WretchedWhispers.Engine./g' {} +
```

- [ ] **Step 3: Restore the implicit usings the Web SDK was providing**

The moved files compiled under `Microsoft.NET.Sdk.Web`, which implicitly imports `Microsoft.Extensions.{DependencyInjection,Configuration,Logging}` etc. A classlib does not. Add these `using` lines (top of file, alphabetical with the existing ones):

- `WretchedWhispers.Engine/Configuration/AgentConfiguration.cs`:
  ```csharp
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  ```
- `WretchedWhispers.Engine/Services/TurnCoordinator.cs` (uses `ILogger<TurnCoordinator>`):
  ```csharp
  using Microsoft.Extensions.Logging;
  ```
- Any further file the build flags with CS0246/CS1061 for `IServiceCollection`, `IConfiguration`, `ILogger`, or `GetValue`: add the matching `Microsoft.Extensions.*` using. Do NOT add package references beyond those already in the Engine csproj — all of these flow from Infrastructure/EF transitively plus `Microsoft.Extensions.Options.ConfigurationExtensions`.

- [ ] **Step 4: Re-point consumer usings in Api, Tests, Evals**

The namespaces `…Api.Services`, `…Api.GameTools[.Models]`, `…Api.Prompts` moved wholesale — a blanket sed is safe for them:

```bash
grep -rl "WretchedWhispers\.Api\.\(Services\|GameTools\|Prompts\)" \
  WretchedWhispers.Api WretchedWhispers.Tests WretchedWhispers.Evals --include="*.cs" \
  | xargs sed -i 's/WretchedWhispers\.Api\.\(Services\|GameTools\|Prompts\)/WretchedWhispers.Engine.\1/g'
```

Two namespaces SPLIT and need manual using edits (keep old + add new where both sides are used):

| File | Change |
|---|---|
| `WretchedWhispers.Api/Endpoints/SettingsEndpoints.cs` | `using WretchedWhispers.Api.Configuration;` → `using WretchedWhispers.Engine.Configuration;` (only uses `DesktopLlmOptions`) |
| `WretchedWhispers.Api/Program.cs` | Keep `using WretchedWhispers.Api.Configuration;` (OpenTelemetryConfiguration stays), ADD `using WretchedWhispers.Engine.Configuration;` (AddGameAgent) |
| `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs` | Keep `using WretchedWhispers.Api.Models;` (ChatMessageDto, SessionDetailDto…), ADD `using WretchedWhispers.Engine.Models;` (GameTurnEvent for SSE serialization) |
| `WretchedWhispers.Evals/Harness/EvalSupport.cs` | `using WretchedWhispers.Api.Models;` → `using WretchedWhispers.Engine.Models;` (uses GameTurnEvent only) |
| `WretchedWhispers.Evals/Harness/EvalTurnRunner.cs` | `using WretchedWhispers.Api.Models;` → `using WretchedWhispers.Engine.Models;` (uses GameTurnEvent only) |
| Any Tests file the build flags on `Api.Models` types | If it uses GameTurnEvent/TurnDelta/StateUpdate/AzureOpenAiSettings → switch to `WretchedWhispers.Engine.Models`; if it also uses ChatMessageDto/wire DTOs → keep both usings |
| `WretchedWhispers.Api/Configuration/OpenTelemetryConfiguration.cs` | If it references moved types (e.g. TraceExporter in `Engine.Services`), the sed above already fixed it; otherwise leave alone |

- [ ] **Step 5: Move the package/project references in Api.csproj**

In `WretchedWhispers.Api/WretchedWhispers.Api.csproj`:

Replace:
```xml
    <ProjectReference Include="..\WretchedWhispers.Infrastructure\WretchedWhispers.Infrastructure.csproj" />
```
with:
```xml
    <ProjectReference Include="..\WretchedWhispers.Engine\WretchedWhispers.Engine.csproj" />
```
(Infrastructure and Core still flow transitively — ProjectReference is transitive in SDK-style projects.)

Delete these four lines (now owned by Engine):
```xml
    <PackageReference Include="Azure.AI.OpenAI" Version="2.9.0-beta.1" />
    <PackageReference Include="OpenAI" Version="2.10.0" />
    <PackageReference Include="Microsoft.Agents.AI" Version="1.9.0" />
    <PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.9.0" />
```
Keep all OpenTelemetry packages and the conditional Photino block — they are hosting concerns.

- [ ] **Step 6: Build and fix residual compile errors**

```bash
dotnet build WrtechedWhispers.sln --nologo -v q
```

Expected: likely a handful of CS0246 (missing using) errors on the first run. Fix each ONLY by adjusting `using` lines per the tables above. Re-run until: 0 errors. If an error suggests a wire DTO is needed by Engine code, stop and re-check — per the spec that DTO moves to `WretchedWhispers.Engine/Models/` (namespace `WretchedWhispers.Engine.Models`) rather than Engine referencing Api.

- [ ] **Step 7: Run the test suite**

```bash
dotnet test WretchedWhispers.Tests/WretchedWhispers.Tests.csproj --no-build
```

Expected: 409 passed, 0 failed. (Tests reach Engine types transitively through the Api project reference; explicit refs are tidied in Task 3.)

- [ ] **Step 8: Commit**

```bash
cd /home/arst/Projects/wretched_whispers
git add -A WrtechedWhispers
git commit -m "refactor(engine): move AI layer from Api to Engine class library

Services/, GameTools/, Prompts/, GameTurnEvent, AzureOpenAiSettings and the
agent DI configuration move to WretchedWhispers.Engine with a mechanical
namespace rename. Api keeps only HTTP/hosting concerns and now references
Engine (Core <- Infrastructure <- Engine <- Api). No behavior change.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Re-point Tests and Evals project references

**Files:**
- Modify: `WretchedWhispers.Tests/WretchedWhispers.Tests.csproj:23-28` (ItemGroup with ProjectReferences)
- Modify: `WretchedWhispers.Evals/WretchedWhispers.Evals.csproj` (ProjectReference ItemGroup)

**Interfaces:**
- Consumes: Engine project populated by Task 2.
- Produces: honest project references — Tests declares Engine explicitly (it tests Engine classes directly), Evals drops its Api reference entirely (it exercises prompts/tools, not endpoints), stale Semantic reference deleted.

- [ ] **Step 1: Fix Tests references**

In `WretchedWhispers.Tests/WretchedWhispers.Tests.csproj`, the ProjectReference ItemGroup becomes:

```xml
    <ItemGroup>
        <ProjectReference Include="..\WretchedWhispers.Core\WretchedWhispers.Core.csproj" />
        <ProjectReference Include="..\WretchedWhispers.Infrastructure\WretchedWhispers.Infrastructure.csproj" />
        <ProjectReference Include="..\WretchedWhispers.Engine\WretchedWhispers.Engine.csproj" />
        <ProjectReference Include="..\WretchedWhispers.Api\WretchedWhispers.Api.csproj" />
    </ItemGroup>
```

(This deletes the stale `WretchedWhispers.Semantic` reference — the project was removed with Semantic Kernel. Api stays: `SessionStreamingTests` drives endpoints via `WebApplicationFactory`.)

- [ ] **Step 2: Fix Evals references**

In `WretchedWhispers.Evals/WretchedWhispers.Evals.csproj`, replace:

```xml
    <ProjectReference Include="..\WretchedWhispers.Api\WretchedWhispers.Api.csproj" />
```
with:
```xml
    <ProjectReference Include="..\WretchedWhispers.Engine\WretchedWhispers.Engine.csproj" />
```

Leave the linked `appsettings.json` item alone — `..\WretchedWhispers.Api\appsettings.json` still exists.

- [ ] **Step 3: Build and test**

```bash
dotnet build WrtechedWhispers.sln --nologo -v q
dotnet test WretchedWhispers.Tests/WretchedWhispers.Tests.csproj --no-build
```

Expected: 0 errors; 409 passed. If Evals fails to compile because a harness file uses an Api-only type, that contradicts the verified import survey (Evals imports only Engine + Core + Infrastructure namespaces) — investigate before adding the Api reference back.

- [ ] **Step 4: Commit**

```bash
git add WretchedWhispers.Tests/WretchedWhispers.Tests.csproj WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
git commit -m "refactor(engine): re-point Tests/Evals references, drop stale Semantic ref

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Verify the desktop build end-to-end

**Files:** none modified (verification only; `dist/` and `wwwroot/` outputs are build artifacts).

**Interfaces:**
- Consumes: the finished refactor.
- Produces: proof that the shipping artifact still builds — the desktop packaging exercises the full chain (Next.js static export → wwwroot → self-contained publish with `-p:DesktopBuild=true`, which compiles the `#if DESKTOP` branch and Photino shell that a plain `dotnet build` skips).

- [ ] **Step 1: Run the desktop packaging script**

```bash
cd /home/arst/Projects/wretched_whispers
./build-desktop.sh
```

Expected: all three phases succeed, ending with `==> Done → /home/arst/Projects/wretched_whispers/dist/<rid>`. This is the step that catches a missed `using`/reference inside `Desktop/DesktopHost.cs` or `Program.cs`'s `#if DESKTOP` branch, which normal builds exclude.

- [ ] **Step 2: Confirm the working tree is clean and nothing regressed**

```bash
git status
dotnet build WrtechedWhispers/WrtechedWhispers.sln --nologo -v q
```

Expected: only untracked/ignored build outputs (if any); build 0 errors. No commit needed unless Step 1 forced a fix — if it did, commit the fix with:

```bash
git add -A WrtechedWhispers
git commit -m "fix(engine): include desktop-conditional code in namespace rename

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
