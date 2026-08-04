using System.Net.Http.Json;
using System.Text.Json;

namespace WretchedWhispers.Tests.Sessions;

/// <summary>Registers a fresh user via the identity endpoints and returns its bearer token.
/// The web-app factories share one database per test class, so every caller must use a
/// unique email.</summary>
internal static class AuthFlow
{
    public const string Password = "darkdoom42";

    public static async Task<string> RegisterAndLogin(HttpClient client, string email, string password = Password)
    {
        await client.PostAsJsonAsync("/api/auth/register", new { email, password });

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login?useCookies=false", new { email, password });

        var loginJson = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return loginJson.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("login returned no accessToken");
    }
}
