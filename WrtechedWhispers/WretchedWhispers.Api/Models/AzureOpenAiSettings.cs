namespace WretchedWhispers.Api.Models;

public sealed class AzureOpenAiSettings
{
    public string ChatModelDeployment { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public string ApiKey { get; init; } = "";
}
