using System.Text.Json;
using WretchedWhispers.Engine.Configuration;

namespace WretchedWhispers.Api.Deployment;

public static class StandaloneHost
{
    public static string DataDir { get; } = CreateDataDir();
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");

    public static Dictionary<string, string?> BuildConfig()
    {
        var config = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = "openai",
            ["Llm:Model"] = DesktopLlmOptions.DefaultModel,
        };

        // This layer is added AFTER the default environment-variable provider, so seeding the key
        // unconditionally would mask ConnectionStrings__Default — the standard ASP.NET variable,
        // and one of the two the postgres startup guard tells the user to set.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Default")))
            config["ConnectionStrings:Default"] = $"Data Source={Path.Combine(DataDir, "wretched-whispers.db")}";

        if (!File.Exists(SettingsPath)) return config;

        try
        {
            var settings = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(SettingsPath));
            if (!string.IsNullOrWhiteSpace(settings?.ApiKey)) config["Llm:ApiKey"] = settings.ApiKey;
            if (!string.IsNullOrWhiteSpace(settings?.Model)) config["Llm:Model"] = settings.Model;
            if (!string.IsNullOrWhiteSpace(settings?.BaseUrl)) config["Llm:BaseUrl"] = settings.BaseUrl;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt file is treated as first run so the settings screen can repair it.
        }

        return config;
    }

    private static string CreateDataDir()
    {
        var configured = Environment.GetEnvironmentVariable("WW_DATA_DIR");
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WretchedWhispers")
            : configured;
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The settings.json contract — written by the settings endpoint, read back here on startup.</summary>
    public sealed record PersistedSettings(string? Provider, string? ApiKey, string? Model, string? BaseUrl);
}
