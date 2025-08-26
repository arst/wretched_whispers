using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace WretchedWhispers.Console;

public class Settings
{
    private readonly IConfigurationRoot _configRoot;

    private AzureOpenAiSettings _azureOpenAi;

    public AzureOpenAiSettings AzureOpenAi => this._azureOpenAi ??= this.GetSettings<Settings.AzureOpenAiSettings>();

    public class AzureOpenAiSettings
    {
        public string ChatModelDeployment { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }

    public TSettings GetSettings<TSettings>() =>
        this._configRoot.GetRequiredSection(typeof(TSettings).Name).Get<TSettings>()!;

    public Settings()
    {
        _configRoot =
            new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
                .Build();
    }
}