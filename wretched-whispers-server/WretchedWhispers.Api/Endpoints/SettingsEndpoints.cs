using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using WretchedWhispers.Api.Deployment;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Engine.Configuration;

namespace WretchedWhispers.Api.Endpoints;

/// <summary>
/// Standalone first-run settings: the user pastes their own OpenAI-compatible key (OpenAI, OpenRouter,
/// …). Mapped only with local single-user auth — but onto the authenticated group all the same, so a
/// container that binds 0.0.0.0 does not hand a stranger the ability to repoint the model at their own
/// server. GET never returns the key, only whether one is set.
/// </summary>
public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsEndpoints(
        this RouteGroupBuilder api, string settingsFilePath, bool readOnly = false)
    {
        var group = api.MapGroup("/settings");

        group.MapGet("/", (DesktopLlmOptions opt) => TypedResults.Ok(Describe(opt)));

        group.MapPost("/", async Task<Results<Ok<SettingsDto>, ProblemHttpResult>> (
            DesktopSettingsRequest req, DesktopLlmOptions opt) =>
        {
            // Multi-instance (postgres) mode: a save would configure one random instance and write
            // its local settings.json — silently lost on redeploy. Env vars are the one shared path.
            if (readOnly)
                return ApiProblem.Conflict(
                    "LLM settings are managed via environment variables (OPENAI_API_KEY, OPENAI_MODEL, OPENAI_BASE_URL) in multi-instance mode.");

            var baseUrl = req.BaseUrl?.Trim() ?? "";
            // Caught here rather than as a UriFormatException on the player's first turn.
            if (baseUrl.Length > 0 && !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
                return ApiProblem.BadRequest("The base URL must be absolute, e.g. https://api.openai.com/v1.");

            // A blank key on re-save means "keep the current key" — so the user can reopen settings to
            // change only the model or base URL without re-pasting (and re-exposing) their key.
            var current = opt.Snapshot();
            var key = string.IsNullOrWhiteSpace(req.ApiKey) ? current.ApiKey : req.ApiKey.Trim();
            var model = string.IsNullOrWhiteSpace(req.Model) ? StandaloneHost.DefaultModel : req.Model.Trim();

            // Persist before publishing in memory: if the write fails, the process keeps running the
            // configuration that is actually on disk rather than one that dies at the next restart.
            await WriteAtomically(
                settingsFilePath, new StandaloneHost.PersistedSettings("openai", key, model, baseUrl));
            opt.Update(key, model, baseUrl);

            return TypedResults.Ok(Describe(opt));
        });

        return group;
    }

    private static SettingsDto Describe(DesktopLlmOptions opt)
    {
        var (_, model, baseUrl) = opt.Snapshot();
        return new SettingsDto("openai", model, baseUrl, opt.HasKey);
    }

    /// <summary>
    /// Write-then-rename. A direct write that is interrupted leaves a truncated settings.json, which
    /// <see cref="StandaloneHost.BuildConfig"/> reads as first run — silently losing the user's key.
    /// No CancellationToken, for the same reason: a cancelled half-write is the failure being avoided.
    /// </summary>
    private static async Task WriteAtomically(string path, StandaloneHost.PersistedSettings settings)
    {
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(settings));
        File.Move(temporaryPath, path, overwrite: true);
    }
}

public sealed record DesktopSettingsRequest(string? ApiKey, string? Model, string? BaseUrl);
