using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using WretchedWhispers.Console;
using WretchedWhispers.Core;
using WretchedWhispers.Core.CharacterCreation;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Services;
using WretchedWhispers.Semantic;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

Settings settings = new();

Kernel BuildCampaignKernel()
{
    var kb = Kernel.CreateBuilder();
    RegisterServices(kb);
    var kernel = kb.Build();

    kernel.ImportPluginFromType<CharacterPlugin>("Character");
    kernel.ImportPluginFromType<CampaignPlugin>("Campaign");
    kernel.ImportPluginFromType<EncounterPlugin>("Encounter");
    kernel.ImportPluginFromType<DicePlugin>("Dice");

    return kernel;
}

var campaignKernel = BuildCampaignKernel();

var chatCompletionService = campaignKernel.GetRequiredService<IChatCompletionService>();

var history = new ChatHistory();
history.AddSystemMessage(
    "You're a Game Master that leads games in Mork Borg setting. You have all the tools available for you to lead the game, use the, to create characters, roll dices, challenge characters and so on. To create character use CreateCharacter function.");
history.AddUserMessage("Let's create a character!");
var initialMessage = await chatCompletionService.GetChatMessageContentsAsync(history,
    new AzureOpenAIPromptExecutionSettings
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
    }, campaignKernel);
foreach (var message in initialMessage) Console.WriteLine(message.Content);

Console.WriteLine(initialMessage);

return;

void RegisterServices(IKernelBuilder builder)
{
    builder.AddAzureOpenAIChatCompletion(
        deploymentName: settings.AzureOpenAi.ChatModelDeployment,
        endpoint: settings.AzureOpenAi.Endpoint,
        apiKey: settings.AzureOpenAi.ApiKey);

    builder.Services.AddSingleton<IRandomService>(_ => new SeededRandomService());
    builder.Services.AddSingleton<ICharactersRepository, CharactersRepository>();
    builder.Services.AddSingleton<ICharacterCreationService, CharacterCreationService>();
    builder.Services.AddSingleton<ICampaignsRepository, CampaignsRepository>();
    builder.Services.AddSingleton<IEncountersRepository, EncountersRepository>();
    builder.Services.AddSingleton<CharacterService>();
    builder.Services.AddSingleton<EncounterService>();
    builder.Services.AddSingleton<CampaignService>();
    builder.Services.AddLogging(lb =>
    {
        lb.AddConsole();
        lb.SetMinimumLevel(LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel", LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel.Agents", LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel.Agents.Orchestration", LogLevel.Trace);
    });
}