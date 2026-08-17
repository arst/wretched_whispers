using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Api;
using WretchedWhispers.Api.Auth;
using WretchedWhispers.Api.Configuration;
using WretchedWhispers.Api.Deployment;
using WretchedWhispers.Api.Endpoints;
using WretchedWhispers.Api.Health;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Engine.Configuration;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

// The standalone flavours are launched from arbitrary working directories (a double-clicked desktop
// binary most of all), and the bundled UI sits in wwwroot next to the executable — so the content
// root must be the executable's directory, not the default CWD, or the SPA silently isn't served.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = DeploymentProfile.UsesLocalAuth ? AppContext.BaseDirectory : null,
});

if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets(typeof(ServiceCollectionExtensions).Assembly, optional: true);

if (DeploymentProfile.UsesLocalAuth)
    builder.Configuration.AddInMemoryCollection(StandaloneHost.BuildConfig());

builder.Configuration.AddInMemoryCollection(
    EnvConfigOverrides.Map(Environment.GetEnvironmentVariable));

// Only the hosted Server profile serves a separately-origined web app, and only in development —
// elsewhere the UI ships from wwwroot, same origin. Registered under the same condition as UseCors.
if (DeploymentProfile.UsesIdentity && builder.Environment.IsDevelopment())
{
    var webOrigin = builder.Configuration["Cors:WebOrigin"] ?? "http://localhost:3000";
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(webOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

if (DeploymentProfile.UsesIdentity && !builder.Environment.IsDevelopment())
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var usePostgres = string.Equals(
    builder.Configuration["WW_DB_PROVIDER"], "postgres", StringComparison.OrdinalIgnoreCase);
var connectionString = builder.Configuration.GetConnectionString("Default");

void ConfigureDb(DbContextOptionsBuilder options)
{
    if (usePostgres) options.UseNpgsql(connectionString);
    else options.UseSqlite(connectionString);
    if (builder.Environment.IsDevelopment())
        options.EnableDetailedErrors().EnableSensitiveDataLogging();
}

if (usePostgres)
{
    if (connectionString is null || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "WW_DB_PROVIDER=postgres requires a PostgreSQL connection string via WW_DB_CONNECTION or ConnectionStrings__Default.");
    builder.Services.AddDbContext<WretchedWhispersDbContext, PostgresWwDbContext>(ConfigureDb);
    builder.Services.AddScoped<ISessionLock, PostgresSessionLock>();
}
else
{
    builder.Services.AddDbContext<WretchedWhispersDbContext>(ConfigureDb);
    builder.Services.AddSingleton<ISessionLock, InMemorySessionLock>();
}

if (DeploymentProfile.UsesIdentity)
    builder.Services.AddDataProtection().PersistKeysToDbContext<WretchedWhispersDbContext>();

builder.Services.AddDomainServices();

if (DeploymentProfile.UsesLocalAuth)
{
    builder.Services.AddAuthentication(LocalAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, LocalAuthHandler>(LocalAuthHandler.SchemeName, null);
}
else
{
    builder.Services.AddIdentityApiEndpoints<IdentityUser>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 8;
            options.SignIn.RequireConfirmedEmail = false;
            options.SignIn.RequireConfirmedAccount = false;
        })
        .AddEntityFrameworkStores<WretchedWhispersDbContext>();

    builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
    {
        options.BearerTokenExpiration = TimeSpan.FromMinutes(60);
        options.RefreshTokenExpiration = TimeSpan.FromDays(14);
    });
}

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// One error contract for the whole surface. MapIdentityApi already answers in RFC 9457
// ProblemDetails; without this our own handlers answered in an ad-hoc {"error": "..."} shape and
// unhandled exceptions returned a bodiless 500 in production.
builder.Services.AddProblemDetails();
// ponytail: no AddValidation(). Both free-text fields need trim-then-check and answer in the game's
// own voice, which DataAnnotations can't express — PlayerInput is the single place that does it.
builder.Services.AddOpenApi();

// A turn costs a model call and registration is open, so both are worth bounding. The policies are
// always registered — the endpoints carry the metadata unconditionally — but the middleware that
// enforces them is only added for the hosted multi-user profile, and not in Development, where every
// request shares one address and the limiter would only ever throttle the developer.
var useRateLimiting = DeploymentProfile.UsesIdentity && !builder.Environment.IsDevelopment();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Turns, http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy(RateLimitPolicies.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

if (DeploymentProfile.UsesIdentity)
{
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
}

builder.AddWretchedWhispersOpenTelemetry();
builder.Services.AddGameAgent(builder.Configuration);

var app = builder.Build();
var uiIndex = app.Environment.WebRootFileProvider.GetFileInfo("index.html");

if (DeploymentProfile.UsesIdentity && app.Environment.IsProduction() && !uiIndex.Exists)
    throw new InvalidOperationException(
        "The Server profile requires the bundled UI at wwwroot/index.html in Production.");

// Postgres is migrated out of band by WretchedWhispers.Migrations (see docs/database-migrations.md);
// local SQLite migrates itself so a dev's database can't fall behind the code.
if (DeploymentProfile.UsesLocalAuth || (app.Environment.IsDevelopment() && !usePostgres))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
    await db.Database.MigrateAsync();
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
        .LogInformation("Database migrated successfully");
}

if (TraceExportCommand.Matches(args))
{
    await TraceExportCommand.RunAsync(app.Services, args);
    return;
}

// Turns any unhandled exception into a ProblemDetails 500 instead of a bodiless one, and gives
// bodiless framework responses (401/404/429) the same shape as our own errors.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (DeploymentProfile.UsesIdentity && !app.Environment.IsDevelopment())
    app.UseForwardedHeaders();

if (uiIndex.Exists)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

if (DeploymentProfile.UsesIdentity && app.Environment.IsDevelopment())
    app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// CSRF for the browser-cookie clients. Only the hosted profile has cookies to forge — the standalone
// profiles authenticate every request through LocalAuthHandler, with no ambient credential a third
// party could ride. Endpoints opt in by carrying RequireAntiforgery metadata.
if (DeploymentProfile.UsesIdentity)
    app.UseAntiforgery();

if (useRateLimiting)
    app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

if (DeploymentProfile.UsesIdentity)
{
    var auth = app.MapGroup("/api/auth").RequireRateLimiting(RateLimitPolicies.Auth);
    auth.MapIdentityApi<IdentityUser>();
    auth.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) =>
        TypedResults.Ok(new CsrfTokenDto(antiforgery.GetAndStoreTokens(http).RequestToken ?? "")))
        .RequireAuthorization();
    // Register and login can't carry a token — the user has no identity to bind one to yet — so only
    // logout opts in, rather than the whole /api/auth group.
    auth.MapPost("/logout", async (SignInManager<IdentityUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return TypedResults.Ok();
    }).RequireAuthorization().RequireAntiforgery();
    auth.MapGet("/me", (HttpContext http) =>
        TypedResults.Ok(new CurrentUserDto(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "")))
        .RequireAuthorization();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

var api = app.MapAuthenticatedApi();
api.MapSessionEndpoints();
api.MapTurnEndpoints();

// Under the authenticated group like everything else: the standalone container binds 0.0.0.0, and an
// unauthenticated POST here can repoint Llm:BaseUrl at an attacker's server — i.e. redirect every
// prompt. Local auth makes this free (LocalAuthHandler authenticates every request).
if (DeploymentProfile.UsesSettings)
    api.MapSettingsEndpoints(StandaloneHost.SettingsPath, readOnly: usePostgres);

if (uiIndex.Exists)
{
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var route = context.Request.Path.Value?.Trim('/') ?? "";
        var routeIndex = app.Environment.WebRootFileProvider.GetFileInfo($"{route}/index.html");
        var file = routeIndex.Exists ? routeIndex : uiIndex;
        context.Response.ContentType = "text/html; charset=utf-8";
        // The IFileInfo overload, not PhysicalPath: a non-physical web root (embedded resources in a
        // future single-file publish) has no physical path, and dereferencing it would be an NRE.
        await context.Response.SendFileAsync(file, context.RequestAborted);
    });
}

#if DEPLOYMENT_DESKTOP
if (DeploymentProfile.OpensDesktopShell)
{
    var desktopUrl = $"http://127.0.0.1:{GetFreePort()}";
    app.Urls.Add(desktopUrl);
    await app.StartAsync();
    WretchedWhispers.Api.Desktop.DesktopShell.Run(desktopUrl);
    await app.StopAsync();
}
else
#endif
{
    app.Run();
}

#if DEPLOYMENT_DESKTOP
static int GetFreePort()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port; // ponytail: negligible TOCTOU window; fine for a single-user local app
}
#endif

public partial class Program { }
