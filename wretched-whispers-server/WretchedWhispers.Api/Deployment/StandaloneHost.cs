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
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(DataDir, "wretched-whispers.db")}",
            ["Llm:Provider"] = "openai",
            ["Llm:Model"] = "gpt-4o",
        };

        if (!File.Exists(SettingsPath)) return config;

        try
        {
            var settings = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(SettingsPath));
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

    private sealed record Persisted(string? Provider, string? ApiKey, string? Model, string? BaseUrl);
}
