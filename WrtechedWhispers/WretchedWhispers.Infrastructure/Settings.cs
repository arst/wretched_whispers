using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace WretchedWhispers.Infrastructure;

public class Settings
{
    private readonly IConfigurationRoot _configRoot = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .AddUserSecrets(Assembly.GetExecutingAssembly(), true)
        .Build();

    private AzureOpenAiSettings? _azureOpenAi;

    public AzureOpenAiSettings AzureOpenAi => _azureOpenAi ??= GetSettings<AzureOpenAiSettings>();

    public TSettings GetSettings<TSettings>()
    {
        return _configRoot.GetRequiredSection(typeof(TSettings).Name).Get<TSettings>()!;
    }

    public class AzureOpenAiSettings
    {
        public string ChatModelDeployment { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}