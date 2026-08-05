using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Api.Auth;
using WretchedWhispers.Api.Configuration;
using WretchedWhispers.Api.Deployment;
using WretchedWhispers.Api.Endpoints;
using WretchedWhispers.Api.Health;
using WretchedWhispers.Engine.Configuration;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets(typeof(ServiceCollectionExtensions).Assembly, optional: true);

if (DeploymentProfile.UsesLocalAuth)
    builder.Configuration.AddInMemoryCollection(StandaloneHost.BuildConfig());

builder.Configuration.AddInMemoryCollection(
    EnvConfigOverrides.Map(Environment.GetEnvironmentVariable));

var webOrigin = builder.Configuration["Cors:WebOrigin"] ?? "http://localhost:3000";
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(webOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

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

if (args is ["export-traces", ..])
{
    var outDir = args.Length > 1 ? args[1] : "./traces-export";
    await TraceExporter.ExportAsync(app.Services, outDir);
    return;
}

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

if (DeploymentProfile.UsesIdentity)
{
    var auth = app.MapGroup("/api/auth");
    auth.MapIdentityApi<IdentityUser>();
    auth.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) =>
        Results.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken }))
        .RequireAuthorization();
    auth.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery,
        SignInManager<IdentityUser> signInManager) =>
    {
        if (!await antiforgery.IsRequestValidAsync(http)) return Results.BadRequest();
        await signInManager.SignOutAsync();
        return Results.Ok();
    }).RequireAuthorization();
    auth.MapGet("/me", (HttpContext http) =>
        Results.Ok(new { userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) }))
        .RequireAuthorization();
}

if (DeploymentProfile.UsesSettings)
    app.MapSettingsEndpoints(StandaloneHost.SettingsPath, readOnly: usePostgres);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapGet("/health", () => Results.Ok("alive"));
app.MapSessionEndpoints();
app.MapTurnEndpoints();

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
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(file.PhysicalPath!, context.RequestAborted);
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
