using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Api.Configuration;
using WretchedWhispers.Api.Endpoints;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add user secrets from Infrastructure assembly (where AzureOpenAI keys are stored)
builder.Configuration.AddUserSecrets(typeof(ServiceCollectionExtensions).Assembly, optional: true);

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

// Make Program class accessible for WebApplicationFactory in tests
public partial class Program { }
