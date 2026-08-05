using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WretchedWhispers.Tests.Sessions;

/// <summary>
/// A logged-in browser, modelled the way the real web app logs in: an identity cookie plus an
/// antiforgery token on every unsafe request. Each user gets its own HttpClient because each needs
/// its own cookie jar — a shared client can only ever be one user at a time, and the antiforgery
/// token is bound to whoever fetched it.
/// </summary>
internal sealed class TestUser
{
    public const string Password = "darkdoom42";

    private readonly HttpClient _client;
    private readonly string _csrfToken;

    private TestUser(HttpClient client, string csrfToken)
    {
        _client = client;
        _csrfToken = csrfToken;
    }

    /// <summary>Registers a fresh account and logs it in. The web-app factories share one database per
    /// test class, so every caller must use a unique email.</summary>
    public static async Task<TestUser> RegisterAndLogin(
        WebApplicationFactory<Program> factory, string email, string password = Password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostAsJsonAsync("/api/auth/register", new { email, password });

        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password });
        login.EnsureSuccessStatusCode();

        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        return new TestUser(client, csrf.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("csrf endpoint returned no token"));
    }

    public Task<HttpResponseMessage> Get(string url) => _client.GetAsync(url);

    public Task<HttpResponseMessage> Post(string url, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-CSRF-TOKEN", _csrfToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }
}

internal static class AuthFlow
{
    public const string Password = TestUser.Password;

    /// <summary>The bearer half of MapIdentityApi, kept for the one test that pins it. Bearer clients
    /// can read but not mutate: writes need an antiforgery token, which needs a cookie jar.</summary>
    public static async Task<string> RegisterAndLoginWithBearerToken(
        HttpClient client, string email, string password = Password)
    {
        await client.PostAsJsonAsync("/api/auth/register", new { email, password });

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login?useCookies=false", new { email, password });

        var loginJson = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return loginJson.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("login returned no accessToken");
    }
}
