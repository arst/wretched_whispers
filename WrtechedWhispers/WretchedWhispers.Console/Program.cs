using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using WretchedWhispers.Console;
using WretchedWhispers.Infrastructure;
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

ChatCompletionAgent gameMasterAgent =
    new()
    {
        Name = "Game_Master",
        Instructions =
            """
            You are a Game Master that leads games in Mork Borg setting. You have all the tools available for you to lead the game, use them to create characters, roll dices, challenge characters and so on.
            Usually, the session goes like this:
            1. You create a character using CreateCharacter function.
            2. You create a Campaign using CreateCampaign function.
            3. You join the character to the campaign using AddCharacterToCampaign function.
            4. You start the campaign using StartCampaign function.
            5. You start the game by describing what happens, challenging player and so on.
            6. If they meet someone dangerous or potentially dangerous, you create an encounter using CreateEncounter function.
            7. You add adversaries to the encounter using AddAdversariesToEncounter function.
            8. You start the encounter using StartEncounter function.
            9. You describe the encounter and what happens, challenging player or letting them attack adversaries or you adversaries to attack players.
            10. You end the encounter using EndEncounter function when adversaries are no more(no active adversaries in the encounter).
            11. You generate results of the encounter.
            12. You continue the game until the campaign ends.
            13. You can create more encounters, if/when players meet more adversaries.
            14. After each action that take players some time(you decide what time it takes, but no less than 1 hour), you will advance campaign time using AdvanceTime function.
            """,
        Kernel = campaignKernel,
        Arguments =
            new KernelArguments(new AzureOpenAIPromptExecutionSettings
                { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })
    };

ChatHistoryAgentThread agentThread = new();

var isComplete = false;
do
{
    Console.WriteLine();
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) continue;

    if (input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
    {
        isComplete = true;
        break;
    }

    var message = new ChatMessageContent(AuthorRole.User, input);

    await foreach (ChatMessageContent response in gameMasterAgent.InvokeAsync(message, agentThread))
        Console.WriteLine($"{response.Content}");

    Console.WriteLine();
} while (!isComplete);

return;

void RegisterServices(IKernelBuilder builder)
{
    builder.AddAzureOpenAIChatCompletion(
        settings.AzureOpenAi.ChatModelDeployment,
        settings.AzureOpenAi.Endpoint,
        settings.AzureOpenAi.ApiKey);
    builder.Services.AddInMemoryInfrastructure();
    builder.Services.AddLogging(lb =>
    {
        lb.AddConsole();
        lb.SetMinimumLevel(LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel", LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel.Agents", LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel.Agents.Orchestration", LogLevel.Trace);
    });
}