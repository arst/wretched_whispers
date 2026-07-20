using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Api.Auth;
using WretchedWhispers.Api.Configuration;
using WretchedWhispers.Api.Endpoints;
using WretchedWhispers.Engine.Configuration;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add user secrets from Infrastructure assembly (where AzureOpenAI keys are stored)
builder.Configuration.AddUserSecrets(typeof(ServiceCollectionExtensions).Assembly, optional: true);

#if DESKTOP
// Desktop: point SQLite at the writable app-data dir and select the OpenAI provider (key from
// settings.json). Applied before service registration so AddDbContext / AddGameAgent pick it up.
builder.Configuration.AddInMemoryCollection(WretchedWhispers.Api.Desktop.DesktopHost.BuildConfig());
#endif

// CORS: allow Next.js dev server to communicate cross-origin
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// EF Core + SQLite (Scoped lifetime for web API)
builder.Services.AddDbContext<WretchedWhispersDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("Default"));
    // Dev only: turn EF's generic "An error occurred using a transaction" [20205] into the actual
    // failing SQL, parameter values, and the entity/property that faulted. Sensitive data may include
    // player input, so keep it out of production.
    if (builder.Environment.IsDevelopment())
        options.EnableDetailedErrors().EnableSensitiveDataLogging();
});

// Repositories, domain services, dice, JSON options (Scoped)
builder.Services.AddDomainServices();

#if DESKTOP
// Desktop single-user auth: authenticate every request as the fixed local user (no login screen).
// The existing per-user data scoping keeps working with "local" as the tenant.
builder.Services.AddAuthentication(LocalAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, LocalAuthHandler>(LocalAuthHandler.SchemeName, null);
#else
// Identity API endpoints (register, login, refresh)
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

// Configure bearer token expiration
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
{
    options.BearerTokenExpiration = TimeSpan.FromMinutes(60);   // Access token: 1 hour
    options.RefreshTokenExpiration = TimeSpan.FromDays(14);     // Refresh token: 2 weeks
});
#endif

builder.Services.AddAuthorization();

builder.AddWretchedWhispersOpenTelemetry();

builder.Services.AddGameAgent(builder.Configuration);

var app = builder.Build();

// Auto-create/migrate database on first launch
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
    await db.Database.MigrateAsync();
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
        .LogInformation("Database migrated successfully");
}

// Offline CLI: dump all turn traces to JSON for error analysis, then exit (no web host).
//   dotnet run --project WretchedWhispers.Api -- export-traces [outDir]
if (args is ["export-traces", ..])
{
    var outDir = args.Length > 1 ? args[1] : "./traces-export";
    await TraceExporter.ExportAsync(app.Services, outDir);
    return;
}

#if DESKTOP
// Desktop: serve the static-exported SPA from wwwroot same-origin, expose the first-run settings
// endpoints, then open the native window. No CORS (same origin), no Identity endpoints.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapDesktopSettings(WretchedWhispers.Api.Desktop.DesktopHost.SettingsPath);
app.MapGet("/health", () => Results.Ok("alive"));
app.MapSessionEndpoints();
app.MapFallbackToFile("index.html");

var desktopUrl = $"http://127.0.0.1:{GetFreePort()}";
app.Urls.Add(desktopUrl);
await app.StartAsync();
WretchedWhispers.Api.Desktop.DesktopHost.Run(desktopUrl); // blocks until the window closes
await app.StopAsync();

static int GetFreePort()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port; // ponytail: negligible TOCTOU window; fine for a single-user local app
}
#else
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Map Identity endpoints under /auth prefix
app.MapGroup("/auth").MapIdentityApi<IdentityUser>();

// Health check endpoint (no auth)
app.MapGet("/health", () => Results.Ok("alive"));

// Protected endpoint to verify token auth works
app.MapGet("/auth/me", (HttpContext http) =>
    Results.Ok(new { userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) }))
    .RequireAuthorization();

app.MapSessionEndpoints();

app.Run();
#endif

// Make Program class accessible for WebApplicationFactory in tests
public partial class Program { }
