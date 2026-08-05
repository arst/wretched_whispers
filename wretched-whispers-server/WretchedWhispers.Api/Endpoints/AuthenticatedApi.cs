using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Metadata;
using WretchedWhispers.Infrastructure;

namespace WretchedWhispers.Api.Endpoints;

public static class AuthenticatedApi
{
    /// <summary>
    /// The one authenticated entry point every game endpoint hangs off. RequireAuthorization guarantees
    /// an authenticated principal, the first filter guarantees the ambient user scope, the second turns
    /// the antiforgery middleware's verdict into our error contract. Handlers assume all three.
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
            .RequireAntiforgery();
}

public static class AntiforgeryExtensions
{
    /// <summary>
    /// CSRF-protects every unsafe request under this route. UseAntiforgery decides which requests to
    /// check and validates the token; the metadata is how an endpoint opts in, since ASP.NET adds it
    /// automatically only for form binding. Both halves are load-bearing — for a JSON endpoint the
    /// middleware only *records* its verdict, so the filter is what answers on its behalf.
    /// </summary>
    public static TBuilder RequireAntiforgery<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(RequiredMetadata.Instance)
            .AddEndpointFilter(async (context, next) =>
                context.HttpContext.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }
                    ? ApiProblem.BadRequest("Invalid antiforgery token.")
                    : await next(context));

    private sealed class RequiredMetadata : IAntiforgeryMetadata
    {
        public static readonly RequiredMetadata Instance = new();

        public bool RequiresValidation => true;
    }
}
