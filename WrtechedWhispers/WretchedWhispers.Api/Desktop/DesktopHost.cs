using System.Text.Json;
using Photino.NET;

namespace WretchedWhispers.Api.Desktop;

/// <summary>
/// Desktop-build-only host support: per-user app-data paths, loading the persisted LLM settings into
/// configuration on startup, and opening the native Photino window. Compiled only when
/// <c>-p:DesktopBuild=true</c> (see the csproj <c>Compile Remove="Desktop\**"</c> gate), so the hosted
/// web build never references Photino or ships its native webview.
/// </summary>
public static class DesktopHost
{
    public static string DataDir { get; } = CreateDataDir();
    public static string DbPath => Path.Combine(DataDir, "wretched-whispers.db");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");

    private static string CreateDataDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WretchedWhispers");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// In-memory config layer applied before service registration: points SQLite at the writable
    /// app-data dir and selects the OpenAI provider, seeding the key/model from settings.json if present.
    /// </summary>
    public static Dictionary<string, string?> BuildConfig()
    {
        var cfg = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={DbPath}",
            ["Llm:Provider"] = "openai",
            ["Llm:Model"] = "gpt-4o",
        };

        if (File.Exists(SettingsPath))
        {
            try
            {
                var s = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(SettingsPath));
                if (s is not null)
                {
                    if (!string.IsNullOrWhiteSpace(s.ApiKey)) cfg["Llm:ApiKey"] = s.ApiKey;
                    if (!string.IsNullOrWhiteSpace(s.Model)) cfg["Llm:Model"] = s.Model;
                }
            }
            catch
            {
                // Corrupt settings.json → treat the key as unset; the first-run screen lets the user re-enter.
            }
        }

        return cfg;
    }

    /// <summary>Opens the native window and blocks until the user closes it.</summary>
    public static void Run(string url) =>
        new PhotinoWindow()
            .SetTitle("Wretched Whispers")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 860)
            .Center()
            .Load(new Uri(url))
            .WaitForClose();

    private sealed record Persisted(string? Provider, string? ApiKey, string? Model);
}
