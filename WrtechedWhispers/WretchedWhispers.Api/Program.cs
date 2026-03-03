using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Api.Configuration;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// EF Core + SQLite (Scoped lifetime for web API)
builder.Services.AddDbContext<WretchedWhispersDbContext>(
    options => options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

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

var app = builder.Build();

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

app.Run();

// Make Program class accessible for WebApplicationFactory in tests
public partial class Program { }
