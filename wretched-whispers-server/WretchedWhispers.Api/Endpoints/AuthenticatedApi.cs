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
            // Bearer-token API consumers are not vulnerable to CSRF. Browser cookie requests are, so
            // validate only those and leave existing API clients unchanged.
            //
            // Both conditions are needed. A bearer token carries the principal SignInManager built,
            // whose identity is stamped ApplicationScheme — so the scheme alone does NOT distinguish
            // a cookie request from a token one, and testing it by itself makes every bearer client
            // fail antiforgery. The cookie's presence is what actually separates them.
            .AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;
                if (!HttpMethods.IsGet(http.Request.Method)
                    && http.User.Identity?.AuthenticationType == IdentityConstants.ApplicationScheme
                    && http.Request.Cookies.ContainsKey(IdentityCookieName)
                    && !await http.RequestServices.GetRequiredService<IAntiforgery>()
                        .IsRequestValidAsync(http))
                    return ApiProblem.BadRequest("Invalid antiforgery token.");

                return await next(context);
            });

    /// <summary>ASP.NET Identity's default application cookie name. Named here so the coupling is at
    /// least visible: renaming the cookie via ConfigureApplicationCookie must update this too, or
    /// browser requests quietly stop being CSRF-checked.</summary>
    private const string IdentityCookieName = ".AspNetCore.Identity.Application";
}
