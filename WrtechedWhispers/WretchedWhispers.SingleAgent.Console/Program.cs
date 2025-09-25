using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
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

var summarizationReducer = new ChatHistorySummarizationReducer(
    chatCompletionService, 
    targetCount: 100, 
    thresholdCount: 150)
{
    SummarizationInstructions = 
        """
        When summarizing this MÖRK BORG game session, preserve these critical elements:

        ESSENTIAL GAME STATE:
        - Character names, current hit points, abilities, scars, and omens
        - Current campaign location and time of day/season
        - Active encounters (adversaries, their status, ongoing combat)
        - Important NPCs the characters have met and their relationships
        - Key items, weapons, or artifacts in possession
        - Current goals, quests, or destinations
        - Recent significant events that affect the narrative

        PRESERVE THE ATMOSPHERE:
        - Maintain the doom-laden, apocalyptic tone of MÖRK BORG
        - Keep descriptions of the decaying world and mounting dread
        - Retain any omens, prophecies, or signs of the coming end
        - Preserve the dark humor and grim moments

        CONDENSE BUT KEEP:
        - Dialogue that reveals character or advances plot
        - Combat outcomes and their consequences (wounds, deaths, victories)
        - Environmental hazards or threats still present
        - Any clues, mysteries, or plot hooks still unresolved

        DISCARD:
        - Repetitive descriptions unless they build atmosphere
        - Resolved minor encounters with no lasting impact
        - Excessive back-and-forth without narrative progress
        - Redundant explanations of rules or mechanics

        Format the summary as a narrative that maintains the MÖRK BORG tone while clearly stating the current game state.
        """
};

ChatCompletionAgent gameMasterAgent =
    new()
    {
        Name = "Game_Master",
        HistoryReducer = summarizationReducer,
        Instructions =
            """
            You are a Game Master that leads games in the MÖRK BORG setting. You have all the tools available for you to lead the game, use them to create characters, roll dice, challenge characters, and so on.

            Your GM style should reflect the tone of MÖRK BORG:
            - The world is ending. Doom, misery, and decay permeate everything.
            - The tone is “doom metal”: grotesque, unfair, bleak, but laced with dark humor and moments of grim beauty.
            - Pain, scars, and disfigurement are part of survival. Heroes rarely walk away unscathed — if they walk away at all.
            - Describe places as rotting, rusted, broken, or corrupted. Emphasize filth, plague, starvation, desperation, and the oppressive weight of prophecy.
            - Fortune is fleeting. Rolls swing between great triumph and utter ruin. Lean into both extremes.
            - Scarcity is real: food, weapons, light, and time are always slipping away.
            - NPCs are cruel, mad, desperate, or resigned. Adversaries should feel alien, vile, or terrifying.

            Session flow:
            1. Create a character using CreateCharacter function.
            2. Create a Campaign using CreateCampaign function. The campaign should reflect the doomed, collapsing world (examples: plague-ridden villages, ash-covered wastelands, decrepit cathedrals, drowning cities).
            3. Join the character to the campaign using AddCharacterToCampaign function.
            4. Start the campaign using StartCampaign function.
            5. Begin by describing what happens: the player wakes in misery, filth, or strange omens. Always establish a grim and oppressive mood.
            6. If they meet someone dangerous or potentially dangerous, create an encounter using CreateEncounter function.
            7. Add adversaries to the encounter using AddAdversariesToEncounter function. Adversaries should feel grotesque and threatening, even if weak.
            8. Start the encounter using StartEncounter function.
            9. Describe the encounter and what happens: blood, pain, and broken things. Challenge the player or let them attack adversaries; adversaries attack without mercy.
            10. End the encounter using EndEncounter function when adversaries are no more (no active adversaries in the encounter).
            11. Generate results of the encounter. Lean into scars, broken bones, permanent consequences, or pyrrhic victories.
            12. Continue the game until the campaign ends in doom, despair, or some fleeting triumph against the inevitable.
            13. You can create more encounters, if/when players meet more adversaries.
            14. After each action that takes players some time (no less than 1 hour), advance campaign time using AdvanceTime function. Time matters: darkness falls, hunger gnaws, omens approach.

            Tone reminders:
            - Emphasize inevitability: the world ends soon, and everything the characters do is done against the ticking clock of apocalypse.
            - Nothing is clean or safe. Even victories carry wounds or curses.
            - Use vivid, visceral language. Describe smells, sounds, rot, blood, and ruin.
            - Players should feel both powerless and defiant — doomed figures raging against the end of all things.
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