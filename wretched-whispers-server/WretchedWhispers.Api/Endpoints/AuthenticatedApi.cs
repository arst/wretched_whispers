using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using WretchedWhispers.Infrastructure;

namespace WretchedWhispers.Api.Endpoints;

public static class AuthenticatedApi
{
    /// <summary>
    /// The one authenticated entry point every game endpoint hangs off. RequireAuthorization guarantees
    /// an authenticated principal, the first filter guarantees the ambient user scope, the second covers
    /// CSRF for browser cookie requests. Handlers assume all three — no per-handler identity checks.
    /// </summary>
    public static RouteGroupBuilder MapAuthenticatedApi(this WebApplication app) =>
        app.MapGroup("/api")
            .RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;
                var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                http.RequestServices.GetRequiredService<IUserContext>().SetUserId(userId);
                return await next(context);
            })
            // Bearer-token API consumers are not vulnerable to CSRF. Browser cookie requests are,
            // so validate only that authentication scheme and leave existing API clients unchanged.
            .AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;
                if (!HttpMethods.IsGet(http.Request.Method)
                    && http.User.Identity?.AuthenticationType == IdentityConstants.ApplicationScheme
                    && http.Request.Cookies.ContainsKey(".AspNetCore.Identity.Application")
                    && !await http.RequestServices.GetRequiredService<IAntiforgery>()
                        .IsRequestValidAsync(http))
                    return Results.BadRequest(new { error = "Invalid antiforgery token." });

                return await next(context);
            });
}
