using Microsoft.AspNetCore.Http.HttpResults;

namespace WretchedWhispers.Api;

/// <summary>
/// Every error this API returns is RFC 9457 ProblemDetails, so our handlers and the Identity
/// endpoints (which already answer that way) present one contract to the client instead of two.
/// The human-readable text always lives in <c>detail</c>.
/// </summary>
public static class ApiProblem
{
    public static ProblemHttpResult BadRequest(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);

    public static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict);
}

/// <summary>Named so the policy string is declared once and can't drift between the limiter
/// registration and the endpoints that opt in.</summary>
public static class RateLimitPolicies
{
    public const string Turns = "turns";
    public const string Auth = "auth";
}
