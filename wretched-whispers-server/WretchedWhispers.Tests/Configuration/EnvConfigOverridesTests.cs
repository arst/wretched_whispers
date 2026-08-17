using Microsoft.Extensions.Configuration;
using WretchedWhispers.Engine.Configuration;
using Xunit;

namespace WretchedWhispers.Tests.Configuration;

public sealed class EnvConfigOverridesTests
{
    private static string? Env(string name, params (string Name, string Value)[] vars) =>
        vars.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void Map_TranslatesFriendlyNamesToConfigKeys()
    {
        var result = EnvConfigOverrides.Map(n => Env(n,
            ("OPENAI_API_KEY", "sk-test"), ("OPENAI_MODEL", "gpt-5-mini"),
            ("OPENAI_BASE_URL", "https://openrouter.ai/api/v1")));

        Assert.Equal("sk-test", result["Llm:ApiKey"]);
        Assert.Equal("gpt-5-mini", result["Llm:Model"]);
        Assert.Equal("https://openrouter.ai/api/v1", result["Llm:BaseUrl"]);
    }

    [Fact]
    public void Map_TranslatesDbConnectionToConnectionString()
    {
        var result = EnvConfigOverrides.Map(n => Env(n,
            ("WW_DB_CONNECTION", "Host=pg;Database=ww;Username=ww;Password=x")));

        Assert.Equal("Host=pg;Database=ww;Username=ww;Password=x", result["ConnectionStrings:Default"]);
    }

    [Fact]
    public void Map_UnsetOrBlankEnv_MapsNothing()
    {
        Assert.Empty(EnvConfigOverrides.Map(_ => null));
        Assert.Empty(EnvConfigOverrides.Map(_ => "  "));
    }

    [Fact]
    public void Map_LayeredAfterSettingsConfig_EnvWins()
    {
        // Spec precedence pin: the mapping layer is added AFTER the settings.json-seeded
        // in-memory layer (later configuration sources win), so an env key overrides a saved one.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:ApiKey"] = "sk-from-settings" })
            .AddInMemoryCollection(EnvConfigOverrides.Map(n => n == "OPENAI_API_KEY" ? "sk-from-env" : null))
            .Build();

        Assert.Equal("sk-from-env", config["Llm:ApiKey"]);
    }

    [Fact]
    public void Map_PartialEnv_LeavesOtherKeysUntouched()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["Llm:ApiKey"] = "sk-from-settings", ["Llm:Model"] = "gpt-4o" })
            .AddInMemoryCollection(EnvConfigOverrides.Map(n => n == "OPENAI_MODEL" ? "gpt-5-mini" : null))
            .Build();

        Assert.Equal("sk-from-settings", config["Llm:ApiKey"]); // not clobbered by an absent env var
        Assert.Equal("gpt-5-mini", config["Llm:Model"]);
    }
}
