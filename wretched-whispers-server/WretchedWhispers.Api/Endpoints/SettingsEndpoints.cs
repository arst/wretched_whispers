using System.Text.Json;
using WretchedWhispers.Engine.Configuration;

namespace WretchedWhispers.Api.Endpoints;

/// <summary>
/// Standalone first-run settings: the user pastes their own OpenAI-compatible key (OpenAI, OpenRouter,
/// …). Mapped only with local single-user auth, so it needs no additional login. GET never returns the
/// key — only whether one is set.
/// </summary>
public static class SettingsEndpoints
{
    public static WebApplication MapSettingsEndpoints(
        this WebApplication app, string settingsFilePath, bool readOnly = false)
    {
        var group = app.MapGroup("/api/settings");
        group.MapGet("/", (DesktopLlmOptions opt) =>
        {
            var (_, model, baseUrl) = opt.Snapshot();
            return Results.Ok(new { provider = "openai", model, baseUrl, hasKey = opt.HasKey });
        });

        group.MapPost("/", async (DesktopSettingsRequest req, DesktopLlmOptions opt, CancellationToken ct) =>
        {
            // Multi-instance (postgres) mode: a save would configure one random instance and write
            // its local settings.json — silently lost on redeploy. Env vars are the one shared path.
            if (readOnly)
                return Results.Conflict(new
                {
                    error = "LLM settings are managed via environment variables (OPENAI_API_KEY, OPENAI_MODEL, OPENAI_BASE_URL) in multi-instance mode."
                });

            // A blank key on re-save means "keep the current key" — so the user can reopen settings to
            // change only the model or base URL without re-pasting (and re-exposing) their key.
            var current = opt.Snapshot();
            var key = string.IsNullOrWhiteSpace(req.ApiKey) ? current.ApiKey : req.ApiKey.Trim();
            var model = string.IsNullOrWhiteSpace(req.Model) ? "gpt-4o" : req.Model.Trim();
            var baseUrl = req.BaseUrl?.Trim() ?? "";
            opt.Update(key, model, baseUrl);

            var json = JsonSerializer.Serialize(new PersistedSettings("openai", key, model, baseUrl));
            await File.WriteAllTextAsync(settingsFilePath, json, ct);

            return Results.Ok(new { provider = "openai", model, baseUrl, hasKey = opt.HasKey });
        });

        return app;
    }

    // Shape written to app-data settings.json; mirrored by DesktopHost when it loads on startup.
    private sealed record PersistedSettings(string Provider, string ApiKey, string Model, string BaseUrl);
}

public sealed record DesktopSettingsRequest(string? ApiKey, string? Model, string? BaseUrl);
