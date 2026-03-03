# Phase 2: Authentication and Multi-Tenancy - Research

**Researched:** 2026-03-03
**Domain:** ASP.NET Core Identity, Bearer Token Authentication, Multi-Tenant Data Isolation
**Confidence:** HIGH

## Summary

ASP.NET Core 8+ introduced `AddIdentityApiEndpoints<TUser>()` and `MapIdentityApi<TUser>()`, which provide out-of-the-box register, login, refresh, and account management endpoints. These endpoints issue a proprietary bearer token (NOT standard JWT) that behaves identically to JWT from the client's perspective: short-lived access token + refresh token, sent via `Authorization: Bearer <token>` header. This is the recommended approach for SPA backends in the official Microsoft documentation.

The existing `WretchedWhispersDbContext` must be changed to inherit from `IdentityUserContext<IdentityUser>` (no roles needed) instead of plain `DbContext`. This adds Identity tables to the same SQLite database. A new EF Core migration will create the Identity tables alongside existing game tables. Multi-tenancy is achieved by adding a `UserId` (string, matching IdentityUser's key type) column to the `CampaignEntity` table and filtering all campaign queries by the authenticated user's ID.

**Primary recommendation:** Use `AddIdentityApiEndpoints` with the built-in bearer token handler rather than hand-rolling JWT generation. The built-in approach provides register/login/refresh endpoints, token management, and password hashing with zero custom code. The CONTEXT.md specifies "JWT with refresh tokens" -- the bearer token handler satisfies this contract (access token + refresh token, bearer auth header) even though the internal token format is not standard JWT. For a single SPA talking to its own backend, standard JWT provides no advantage.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- ASP.NET Identity -- uses existing EF Core + SQLite stack
- Email/password only, no social login (Discord/Google can be added later)
- No email verification required -- register and play immediately
- Email is just an identifier, not verified
- JWT with refresh tokens -- short-lived access token + longer refresh token
- Stateless auth compatible with Phase 3 API layer and SPA frontend in Phase 4
- Token stored client-side (Phase 4 concern, but JWT decision enables it)
- UserId foreign key on Campaign table
- Filter queries by authenticated user
- Works with existing aggregate structure -- Campaign already has Characters/Encounters as List<Guid>
- Minimal -- no password reset flow, no account lockout policy
- Pre-release with small audience, keep scope tight
- Add recovery features when there are real users

### Claude's Discretion
- JWT token expiry durations (access + refresh)
- Identity table naming/schema choices
- Whether to create a separate Web API project or extend existing infrastructure
- Middleware/filter design for auth enforcement
- Test strategy for auth flows

### Deferred Ideas (OUT OF SCOPE)
- Social login (Discord, Google, GitHub) -- future enhancement
- Email verification -- add when audience grows
- Password reset via email -- requires SMTP, defer to production readiness phase
- Account lockout policy -- defer to production readiness phase
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| AUTH-01 | User can sign up with email and password | `AddIdentityApiEndpoints` provides `/register` endpoint out of the box; IdentityOptions configures password rules |
| AUTH-02 | User can log in and receive JWT authentication token | `/login` endpoint with `useCookies=false` returns bearer access token + refresh token |
| AUTH-03 | User session persists across browser refresh | Refresh token endpoint (`/refresh`) allows obtaining new access token without re-login; client stores tokens |
| INFR-03 | Multi-tenant session isolation (each player's games are private) | UserId FK on CampaignEntity + query filtering by authenticated user's ClaimsPrincipal |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 9.0.* | Identity data model + EF Core integration | Official Microsoft package; IdentityUserContext/IdentityDbContext base classes |
| Microsoft.AspNetCore.Identity (implicit via framework) | 9.0.* | UserManager, SignInManager, Identity services | Included in ASP.NET Core shared framework; no explicit package needed for Web projects |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.* | SQLite provider (already in use) | Already established in Infrastructure project |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.AspNetCore.Authentication.BearerToken | 9.0.* | Bearer token handler for Identity API endpoints | Included automatically by AddIdentityApiEndpoints; handles token generation/validation |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Built-in bearer tokens | Hand-rolled JWT with `System.IdentityModel.Tokens.Jwt` | Standard JWT is needed only for cross-service auth; adds 100+ lines of token generation code, key management, and custom endpoints for zero benefit in a single-SPA-to-backend scenario |
| AddIdentityApiEndpoints | AddIdentityCore + custom endpoints | More control but must hand-write register, login, refresh, password hashing endpoints; Identity API endpoints handle all of this |
| IdentityUserContext (no roles) | IdentityDbContext (with roles) | Roles add 3 extra tables (AspNetRoles, AspNetRoleClaims, AspNetUserRoles) that this project does not need; IdentityUserContext is lighter |

### Clarification on "JWT" vs Bearer Token

The CONTEXT.md specifies "JWT with refresh tokens." The built-in Identity API endpoints issue a **proprietary bearer token** (not standard JWT format) but the client-facing contract is identical:
- Login returns `{ tokenType, accessToken, expiresIn, refreshToken }`
- Client sends `Authorization: Bearer <accessToken>` header
- Refresh endpoint exchanges refresh token for new access/refresh pair
- Tokens are short-lived access + longer-lived refresh

For this project (single SPA frontend talking to its own backend), there is no interoperability need that would require standard JWT format. The bearer token approach is simpler, officially supported, and satisfies all AUTH requirements. If standard JWT becomes needed later (e.g., for microservices), the migration path is well-documented.

**Installation:**
```bash
# Only new package needed (others already in use):
dotnet add WrtechedWhispers/WretchedWhispers.Infrastructure/WretchedWhispers.Infrastructure.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version "9.0.*"
```

Note: The new Web API project (if created) will reference the `Microsoft.NET.Sdk.Web` SDK which includes all ASP.NET Core authentication packages implicitly.

## Architecture Patterns

### Recommended Project Structure

Two viable approaches for the Web API project:

**Option A: New Web API project (RECOMMENDED)**
```
WrtechedWhispers/
├── WretchedWhispers.Core/           # Domain (unchanged)
├── WretchedWhispers.Infrastructure/ # DbContext becomes IdentityUserContext
│   └── Persistence/
│       ├── WretchedWhispersDbContext.cs  # Now inherits IdentityUserContext<IdentityUser>
│       ├── Entities/
│       │   └── CampaignEntity.cs        # Add UserId property
│       ├── Configurations/
│       │   └── CampaignEntityConfiguration.cs  # Add UserId FK + index
│       └── Migrations/
│           └── YYYYMMDD_AddIdentity.cs  # New migration
├── WretchedWhispers.Api/            # NEW: Web API host
│   ├── Program.cs                   # Identity + auth configuration
│   ├── WretchedWhispers.Api.csproj  # Sdk="Microsoft.NET.Sdk.Web"
│   └── appsettings.json
├── WretchedWhispers.Semantic/       # SK integration (unchanged)
├── WretchedWhispers.Tests/          # Add auth integration tests
└── WrtechedWhispers.sln             # Add new project
```

**Option B: Add endpoints to existing Infrastructure** -- not recommended because Infrastructure is a class library, not a Web host. Phase 3 API layer needs a proper Web project anyway. Creating it now avoids rework.

**Recommendation: Option A.** Create a minimal Web API project that hosts Identity endpoints. Phase 3 (API + Streaming) will extend this same project with game endpoints.

### Pattern 1: IdentityUserContext Integration

**What:** Change existing DbContext to inherit from IdentityUserContext instead of DbContext.
**When to use:** When Identity is needed without role-based features.
**Why IdentityUserContext over IdentityDbContext:** No roles needed for this project. IdentityUserContext creates only 4 tables (AspNetUsers, AspNetUserClaims, AspNetUserLogins, AspNetUserTokens) instead of 7.

```csharp
// Source: Microsoft Learn - Identity model customization
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

public class WretchedWhispersDbContext : IdentityUserContext<IdentityUser>
{
    public WretchedWhispersDbContext(DbContextOptions<WretchedWhispersDbContext> options)
        : base(options)
    {
    }

    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<CampaignEntity> Campaigns => Set<CampaignEntity>();
    public DbSet<EncounterEntity> Encounters => Set<EncounterEntity>();
    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // CRITICAL: Must call base to configure Identity table mappings
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WretchedWhispersDbContext).Assembly);
    }
}
```

### Pattern 2: Identity API Endpoint Registration

**What:** Register Identity services and map endpoints in the Web API host.
**When to use:** Program.cs of the new API project.

```csharp
// Source: Microsoft Learn - Use Identity to secure a Web API backend for SPAs
var builder = WebApplication.CreateBuilder(args);

// EF Core + SQLite
builder.Services.AddDbContext<WretchedWhispersDbContext>(
    options => options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Identity API endpoints (register, login, refresh, etc.)
builder.Services.AddIdentityApiEndpoints<IdentityUser>(options =>
    {
        // Relaxed password rules for pre-release
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 8;

        // No email confirmation required
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<WretchedWhispersDbContext>();

builder.Services.AddAuthorization();

var app = builder.Build();

// Map Identity endpoints under /auth prefix
app.MapGroup("/auth").MapIdentityApi<IdentityUser>();

app.UseAuthentication();
app.UseAuthorization();

app.Run();
```

### Pattern 3: Multi-Tenancy via UserId FK

**What:** Add UserId to CampaignEntity and filter queries by authenticated user.
**When to use:** All campaign data access.

```csharp
// CampaignEntity with UserId
public class CampaignEntity
{
    public Guid Id { get; set; }
    public string Data { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty; // FK to AspNetUsers.Id
}

// Configuration
public class CampaignEntityConfiguration : IEntityTypeConfiguration<CampaignEntity>
{
    public void Configure(EntityTypeBuilder<CampaignEntity> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Data).IsRequired().HasColumnType("TEXT");

        // Multi-tenancy FK
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(450);
        builder.HasIndex(e => e.UserId);
    }
}
```

### Pattern 4: Bearer Token Expiration Configuration

**What:** Configure access and refresh token lifetimes.
**When to use:** During Identity service registration.

```csharp
// Source: GitHub dotnet/aspnetcore issue #51047 -- BearerTokenOptions configuration
builder.Services.AddIdentityApiEndpoints<IdentityUser>(
    identityOptions =>
    {
        identityOptions.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<WretchedWhispersDbContext>();

// Configure bearer token expiration separately
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
{
    options.BearerTokenExpiration = TimeSpan.FromMinutes(60);   // Access token: 1 hour
    options.RefreshTokenExpiration = TimeSpan.FromDays(14);     // Refresh token: 2 weeks
});
```

**Recommended durations (Claude's discretion):**
- Access token: **60 minutes** -- long enough to play a session without constant refreshes, short enough for security
- Refresh token: **14 days** -- allows returning to a game after days away without re-login

### Anti-Patterns to Avoid
- **Inheriting from IdentityDbContext when roles are not needed:** Adds 3 unnecessary tables. Use `IdentityUserContext<IdentityUser>` instead.
- **Forgetting `base.OnModelCreating()`:** Identity tables will not be configured, causing runtime errors. MUST call base before custom configuration.
- **Using `AddIdentity` instead of `AddIdentityApiEndpoints`:** AddIdentity configures cookie-based auth and Razor Pages UI, which is wrong for an API backend.
- **Storing UserId in the Campaign domain aggregate:** UserId is an infrastructure/tenancy concern, not a domain concept. Keep it on the persistence entity only.
- **Making auth services Scoped when SK needs Transient:** The existing project uses Transient lifetime for SK compatibility. Auth middleware runs per-request anyway, so this is fine.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| User registration | Custom register endpoint with manual password hashing | `MapIdentityApi` `/register` endpoint | Handles validation, password hashing (PBKDF2), duplicate detection, error responses |
| Login + token issuance | Custom login endpoint with manual credential checking | `MapIdentityApi` `/login` endpoint | Handles lockout, 2FA hooks, token generation, secure comparison |
| Token refresh | Custom refresh token storage and rotation | `MapIdentityApi` `/refresh` endpoint | Handles token rotation, expiration, revocation |
| Password hashing | `BCrypt.Net` or raw `PBKDF2` | ASP.NET Identity's `PasswordHasher<TUser>` | Auto-upgrades hash algorithms, handles salting, timing-safe comparison |
| User management | Custom Users table and CRUD | Identity's `UserManager<TUser>` | Handles normalization, concurrency stamps, email uniqueness |

**Key insight:** ASP.NET Core Identity API endpoints provide a complete authentication system in ~10 lines of configuration. Hand-rolling any piece (especially password handling or token management) introduces security vulnerabilities.

## Common Pitfalls

### Pitfall 1: Forgetting base.OnModelCreating() Call
**What goes wrong:** Identity tables are not created in the database; migrations are empty or fail.
**Why it happens:** When inheriting from IdentityUserContext and overriding OnModelCreating, the base call configures all Identity entity mappings.
**How to avoid:** Always call `base.OnModelCreating(modelBuilder)` as the FIRST line of the override.
**Warning signs:** Migration generates no Identity tables, or runtime errors about missing AspNetUsers table.

### Pitfall 2: DesignTimeDbContextFactory Must Match Runtime Configuration
**What goes wrong:** EF Core migrations generate incorrect schema because design-time factory doesn't configure Identity.
**Why it happens:** The existing `DesignTimeDbContextFactory` creates a plain `WretchedWhispersDbContext` with `UseSqlite`. After changing the DbContext base class, the factory already constructs the correct type -- but if Identity options (like `MaxLengthForKeys`) are set at runtime, they must also be set at design time.
**How to avoid:** Keep the DesignTimeDbContextFactory simple; don't configure Identity-specific options that affect schema outside of OnModelCreating.
**Warning signs:** Migration diff doesn't match expected table changes.

### Pitfall 3: Existing Data Migration with New Required UserId Column
**What goes wrong:** Adding a required `UserId` column to CampaignEntity fails if existing campaign rows have no UserId.
**Why it happens:** SQLite requires a default or migration script to populate existing rows.
**How to avoid:** Either (a) make UserId nullable initially and clean up, or (b) since this is pre-release with test data, delete the existing database and recreate with the new migration.
**Warning signs:** `dotnet ef database update` fails with "NOT NULL constraint" error.
**Recommendation:** Pre-release, no real users. Delete DB and recreate from fresh migration.

### Pitfall 4: Token Format Confusion
**What goes wrong:** Developers try to decode the bearer token as JWT (e.g., on jwt.io) and it fails.
**Why it happens:** The Identity API endpoint token is a Data Protection-encrypted opaque token, not a standard JWT.
**How to avoid:** Document that tokens are proprietary format. Do not attempt to decode or validate them outside of the ASP.NET Core pipeline.
**Warning signs:** Frontend code tries to parse token payload for claims.

### Pitfall 5: Authentication Middleware Order
**What goes wrong:** Authenticated endpoints return 401 even with valid tokens.
**Why it happens:** `UseAuthentication()` must come before `UseAuthorization()` in the middleware pipeline.
**How to avoid:** Always register in this order: `app.UseAuthentication(); app.UseAuthorization();`
**Warning signs:** Token is sent correctly but every request returns 401.

### Pitfall 6: DbContext Constructor Generic Type Mismatch
**What goes wrong:** Runtime DI error or migration tooling fails.
**Why it happens:** `IdentityUserContext<TUser>` constructor expects `DbContextOptions<WretchedWhispersDbContext>` specifically. If using `DbContextOptions` (non-generic), it may not resolve correctly.
**How to avoid:** Keep the existing constructor signature `DbContextOptions<WretchedWhispersDbContext>`.
**Warning signs:** DI resolution error at startup.

## Code Examples

### Complete Registration + Login Flow (Client Perspective)

```
POST /auth/register
Content-Type: application/json
{ "email": "player@example.com", "password": "darkdoom42" }
-> 200 OK (empty body)

POST /auth/login?useCookies=false
Content-Type: application/json
{ "email": "player@example.com", "password": "darkdoom42" }
-> 200 OK
{
  "tokenType": "Bearer",
  "accessToken": "CfDJ8...",
  "expiresIn": 3600,
  "refreshToken": "CfDJ8..."
}

GET /api/campaigns
Authorization: Bearer CfDJ8...
-> 200 OK (user's campaigns only)

POST /auth/refresh
Content-Type: application/json
{ "refreshToken": "CfDJ8..." }
-> 200 OK (new accessToken + refreshToken)
```

### Getting Authenticated User ID in Repository

```csharp
// In the API endpoint or service layer:
// HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) gives the user's Identity ID

// Repository method filtered by user:
public async Task<List<Campaign>> GetCampaignsForUser(string userId)
{
    var entities = await _db.Campaigns
        .Where(c => c.UserId == userId)
        .ToListAsync();

    return entities
        .Select(e => JsonSerializer.Deserialize<Campaign>(e.Data, _jsonOptions)!)
        .ToList();
}
```

### Securing Endpoints

```csharp
// Require auth on specific endpoints
app.MapGet("/api/campaigns", async (
    HttpContext http,
    ICampaignsRepository repo) =>
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var campaigns = await repo.GetCampaignsForUser(userId);
    return Results.Ok(campaigns);
}).RequireAuthorization();

// Or use a fallback policy to require auth on ALL endpoints by default
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| AddIdentity + custom JWT endpoint | AddIdentityApiEndpoints + built-in bearer | .NET 8 (Nov 2023) | Eliminates 100+ lines of boilerplate |
| IdentityServer for simple auth | Built-in Identity API endpoints | .NET 8 (Nov 2023) | No third-party dependency needed for single-app auth |
| Cookie-only Identity | Bearer token option via `useCookies=false` | .NET 8 (Nov 2023) | First-class token auth support in Identity |
| Manual BearerTokenOptions config | Inline configuration via AddIdentityApiEndpoints overload | .NET 9 (Nov 2024) | Cleaner configuration API |

**Deprecated/outdated:**
- `AddDefaultIdentity`: Configures Razor Pages UI and cookies; wrong for API backends
- `JwtSecurityTokenHandler` / `System.IdentityModel.Tokens.Jwt` for self-issued tokens: Unnecessary complexity when Identity API endpoints exist
- IdentityServer (open source): Now commercial (Duende); overkill for single-app auth

## Open Questions

1. **TFM Compatibility**
   - What we know: Projects target net10.0 but only .NET 9 SDK is installed. Build currently fails with NETSDK1045.
   - What's unclear: Whether net10.0 was intentional (preview SDK expected) or should be net9.0.
   - Recommendation: Phase 2 implementation should match whatever TFM the project uses at execution time. If net10.0 SDK is not available, packages should still use 9.0.* versions which are forward-compatible.

2. **Existing Database Data**
   - What we know: There is an existing SQLite database with an InitialCreate migration. Adding Identity changes the DbContext base class.
   - What's unclear: Whether there is valuable test data that should be preserved.
   - Recommendation: Pre-release project with no real users. Delete existing database and create a new combined migration, or add an incremental migration. Both work.

3. **API Project Scope in Phase 2 vs Phase 3**
   - What we know: CONTEXT.md says Phase 3 builds the API layer with SSE streaming. Phase 2 needs at least a minimal Web host for Identity endpoints.
   - What's unclear: How much of the API project structure to build in Phase 2 vs defer to Phase 3.
   - Recommendation: Phase 2 creates a minimal Web API project with Identity endpoints only. Phase 3 extends it with game API endpoints and SSE streaming.

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn: Use Identity to secure a Web API backend for SPAs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0) - AddIdentityApiEndpoints setup, MapIdentityApi endpoints, bearer token vs cookie, complete endpoint list
- [Microsoft Learn: Identity model customization](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0) - IdentityDbContext vs IdentityUserContext, table customization, OnModelCreating requirements, primary key types

### Secondary (MEDIUM confidence)
- [Auth0: Cookies, Tokens, or JWTs? The ASP.NET Core Identity Dilemma](https://auth0.com/blog/cookies-tokens-jwt-the-aspnet-core-identity-dilemma/) - Bearer token format explanation (proprietary, not JWT)
- [Nestenius: BearerToken handler in ASP.NET Core 8](https://nestenius.se/net/bearertoken-the-new-authentication-handler-in-net-8/) - Technical details on bearer token handler
- [GitHub: dotnet/aspnetcore issue #51047](https://github.com/dotnet/aspnetcore/issues/51047) - BearerTokenOptions configuration with AddIdentityApiEndpoints
- [Andrew Lock: Introducing the Identity API endpoints](https://andrewlock.net/exploring-the-dotnet-8-preview-introducing-the-identity-api-endpoints/) - Deep dive on AddIdentityApiEndpoints internals

### Tertiary (LOW confidence)
- None -- all findings verified against official Microsoft documentation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Official Microsoft packages, well-documented APIs, established patterns
- Architecture: HIGH - IdentityUserContext integration with existing DbContext is a documented pattern; two-project approach (Infrastructure + API) follows standard ASP.NET Core conventions
- Pitfalls: HIGH - Well-known issues documented in official docs and verified through multiple community sources
- Token format clarification: HIGH - Confirmed directly from Microsoft documentation that tokens are proprietary, not JWT

**Research date:** 2026-03-03
**Valid until:** 2026-04-03 (stable APIs, unlikely to change)
