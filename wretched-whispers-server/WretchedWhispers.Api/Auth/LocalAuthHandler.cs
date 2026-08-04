using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace WretchedWhispers.Api.Auth;

/// <summary>
/// Desktop single-user authentication: every request is authenticated as the fixed local user
/// ("local"). This makes <c>RequireAuthorization()</c> pass and every endpoint's
/// <c>FindFirstValue(NameIdentifier)</c> / per-user scoping keep working unchanged — no login screen,
/// zero endpoint edits. Registered only for standalone profiles; Server keeps ASP.NET Identity.
/// </summary>
public sealed class LocalAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Local";
    public const string LocalUserId = "local";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, LocalUserId)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
