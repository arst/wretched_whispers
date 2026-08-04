namespace WretchedWhispers.Engine.Configuration;

/// <summary>
/// Maps friendly container env vars (OPENAI_API_KEY, ...) to the Llm:* configuration keys the
/// OpenAI provider reads. Shared by both standalone profiles so it is unit-testable; the
/// desktop/headless Program applies it as the LAST configuration layer so env vars beat the
/// settings.json values seeded by DesktopHost.BuildConfig (spec: env > settings.json > first-run UI).
/// </summary>
public static class EnvConfigOverrides
{
    private static readonly (string Env, string Key)[] Mappings =
    [
        ("OPENAI_API_KEY", "Llm:ApiKey"),
        ("OPENAI_MODEL", "Llm:Model"),
        ("OPENAI_BASE_URL", "Llm:BaseUrl"),
        // Postgres mode: beats DesktopHost.BuildConfig's SQLite path (this layer is applied last).
        ("WW_DB_CONNECTION", "ConnectionStrings:Default"),
    ];

    public static Dictionary<string, string?> Map(Func<string, string?> getEnv)
    {
        var overrides = new Dictionary<string, string?>();
        foreach (var (env, key) in Mappings)
        {
            var value = getEnv(env);
            if (!string.IsNullOrWhiteSpace(value)) overrides[key] = value;
        }
        return overrides;
    }
}
