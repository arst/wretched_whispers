using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration.Handoff;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Semantic;
#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0001

Settings settings = new();


ChatCompletionAgent gameMasterAgent =
    new()
    {
        Name = "Game_Master",
        Instructions =
            """
            You are a Game Master that leads games in the MORK BORG setting. You have all the tools available for you to lead the game, use them to create characters, roll dice, challenge characters, and so on.

            Your GM style should reflect the tone of MORK BORG:
            - The world is ending. Doom, misery, and decay permeate everything.
            - The tone is "doom metal": grotesque, unfair, bleak, but laced with dark humor and moments of grim beauty.
            - Pain, scars, and disfigurement are part of survival. Heroes rarely walk away unscathed -- if they walk away at all.
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
            7. Add adversaries to the encounter using AddAdversaryToEncounter function. Adversaries should feel grotesque and threatening, even if weak.
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
            - Players should feel both powerless and defiant -- doomed figures raging against the end of all things.
            """,
        Kernel = BuildCampaignKernel(applyMigrations: true),
        Arguments =
            new KernelArguments(new AzureOpenAIPromptExecutionSettings
                { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })
    };

ChatCompletionAgent characterBiographyAgent =
    new()
    {
        Name = "Character_Biography_Agent",
        Instructions =
            """
            You are a Character Biography Agent specialized in weaving character stories that capture the brutal, doomed essence of MORK BORG. Your role is to take a character's existing biography and incorporate new events, encounters, and experiences into a cohesive narrative that reflects the world's bleak reality.

            Core Responsibilities:
            1. Receive the character's current biography and new events/encounters to incorporate
            2. Weave these new experiences into the existing narrative seamlessly
            3. Maintain consistency with the character's established personality, background, and motivations
            4. Ensure the updated biography captures the MORK BORG tone: doom, decay, dark humor, and grim beauty

            Writing Style Guidelines:
            - Embrace the "doom metal" aesthetic: grotesque, unfair, bleak, but with moments of dark poetry
            - Focus on how events scar the character physically, mentally, and spiritually
            - Emphasize transformation through suffering - each encounter should leave marks
            - Use visceral, evocative language that describes rot, blood, pain, and corruption
            - Show how the character adapts to or is broken by their experiences
            - Highlight moments of defiance against the inevitable doom
            - Include sensory details: foul smells, agonizing sounds, the weight of despair

            Narrative Integration:
            - Connect new events to existing character traits and motivations
            - Show cause and effect - how past experiences led to current situations
            - Build recurring themes (survival, loss, corruption, fleeting hope)
            - Create callbacks to previous events when relevant
            - Maintain character voice and perspective throughout

            Biography Structure:
            - Start with established background if updating existing biography
            - Chronicle events in roughly chronological order
            - Focus on pivotal moments that define the character
            - End with the character's current state and outlook
            - Include scars, both visible and hidden, that tell their story

            Remember: In MORK BORG, every victory is pyrrhic, every survival comes at a cost, and the world's end approaches with each passing day. The character's biography should reflect this inevitable doom while celebrating their stubborn will to continue existing in spite of it all.
            """,
        Kernel = BuildCampaignKernel(),
        Arguments =
            new KernelArguments(new AzureOpenAIPromptExecutionSettings
                { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })
    };

ChatCompletionAgent campaignLoreAgent =
    new()
    {
        Name = "Campaign_Lore_Agent",
        Instructions =
            """
            You are a Campaign Lore Agent specialized in creating, maintaining, and expanding the dark mythology and world-building elements of MORK BORG campaigns. Your role is to develop rich, interconnected lore that enhances the apocalyptic atmosphere and provides narrative depth to ongoing campaigns.

            Core Responsibilities:
            1. Create compelling backstories and histories for campaign locations, NPCs, and events
            2. Maintain consistency across all campaign lore and world-building elements
            3. Generate atmospheric details that reinforce the doomed, decaying world aesthetic
            4. Connect disparate story elements into cohesive narrative threads
            5. Expand existing lore based on player actions and campaign developments

            World-Building Guidelines:
            - Everything is corrupted, decaying, or cursed in some way
            - History is filled with failed prophecies, fallen kingdoms, and forgotten gods
            - Present a world where hope is scarce but defiance persists
            - Create interconnected mysteries that reveal deeper horrors
            - Emphasize the weight of ancient sins and inevitable consequences

            MORK BORG Tone Elements:
            - Biblical apocalypse mixed with death metal aesthetics
            - Grotesque beauty in decay and corruption
            - Dark humor that highlights life's absurdity
            - Cosmic horror underlying mundane suffering
            - Religious imagery twisted into blasphemous forms
            - Medieval technology corrupted by dark magic

            Lore Creation Focus Areas:
            - Locations: Describe places as monuments to failure, decay, and lost glory
            - NPCs: Create characters shaped by suffering, madness, or desperate purpose
            - Artifacts: Items with dark histories, curses, or terrible prices
            - Events: Historical moments that explain current miseries
            - Religions: Faiths that offer false hope or demand terrible sacrifices
            - Organizations: Groups bound by desperation, corruption, or doomed causes

            Narrative Integration:
            - Connect new lore to existing campaign events and character actions
            - Create recurring themes and motifs across different story elements
            - Build mysteries that reward player investigation while maintaining dread
            - Establish consequences for past events that affect current situations
            - Layer multiple interpretations to create depth and uncertainty

            Writing Style:
            - Use visceral, evocative language that appeals to multiple senses
            - Employ biblical and mythological references with dark twists
            - Create atmosphere through environmental storytelling
            - Balance exposition with implication - let players discover horrors
            - Maintain consistency with established MORK BORG lore and aesthetics

            Remember: The world of MORK BORG is ending, but it's been ending for a long time. Your lore should reflect this prolonged apocalypse - a world that has survived multiple disasters and now faces its final doom. Every location, person, and artifact should tell a story of endurance in the face of inevitable destruction.
            """,
        Kernel = BuildCampaignKernel(),
        Arguments =
            new KernelArguments(new AzureOpenAIPromptExecutionSettings
                { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })
    };

// Optional: stream/trace agent outputs as they speak
var history = new ChatHistory();
ValueTask ResponseCallback(ChatMessageContent msg)
{
    history.Add(msg);
    Console.WriteLine($"\n[{msg.AuthorName}] {msg.Content}");
    return ValueTask.CompletedTask;
}

// Console input whenever an agent needs user input
ValueTask<ChatMessageContent> InteractiveCallback()
{
    Console.Write("\n> ");
    var input = Console.ReadLine() ?? string.Empty;
    return ValueTask.FromResult(new ChatMessageContent(AuthorRole.User, input));
}

var handoffs = OrchestrationHandoffs
    .StartWith(gameMasterAgent)
    .Add(gameMasterAgent, characterBiographyAgent,
        "Hand off when you need to weave new events into a character's biography.")
    .Add(gameMasterAgent, campaignLoreAgent,
        "Hand off when you need campaign/world lore, places, NPC histories, relics, omens, or prophecy context.")
    .Add(characterBiographyAgent, gameMasterAgent,
        "Return handoff when biography updates are complete.")
    .Add(campaignLoreAgent, gameMasterAgent,
        "Return handoff when lore/worldbuilding is provided.");
// Build the handoff orchestration
var orchestration = new HandoffOrchestration(
    handoffs,
    gameMasterAgent,
    characterBiographyAgent,
    campaignLoreAgent)
{
    InteractiveCallback = InteractiveCallback,
    ResponseCallback = ResponseCallback,
};

// Start a runtime and invoke
var runtime = new InProcessRuntime();
await runtime.StartAsync();

var initialTask =
    "Let's start campaign!.";

var result = await orchestration.InvokeAsync(initialTask, runtime);

// wait for completion (tune timeout as you like)
var summary = await result.GetValueAsync(TimeSpan.FromMinutes(2));
Console.WriteLine($"\n=== SUMMARY ===\n{summary}\n");

// (optional) stop runtime after idle
await runtime.RunUntilIdleAsync();


Console.WriteLine("END!");
return;

void RegisterServices(IKernelBuilder builder)
{
    builder.AddAzureOpenAIChatCompletion(
        settings.AzureOpenAi.ChatModelDeployment,
        settings.AzureOpenAi.Endpoint,
        settings.AzureOpenAi.ApiKey);
    builder.Services.AddSqliteInfrastructure(settings.Database.ConnectionString);
    builder.Services.AddLogging(lb =>
    {
        lb.AddConsole();
        lb.SetMinimumLevel(LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel", LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel.Agents", LogLevel.Trace);
        lb.AddFilter("Microsoft.SemanticKernel.Agents.Orchestration", LogLevel.Trace);
    });
}

Kernel BuildCampaignKernel(bool applyMigrations = false)
{
    var kb = Kernel.CreateBuilder();
    RegisterServices(kb);
    var kernel = kb.Build();

    if (applyMigrations)
    {
        // Apply pending migrations on startup (only once)
        using var scope = kernel.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
        db.Database.Migrate();
    }

    kernel.ImportPluginFromType<CharacterPlugin>("Character");
    kernel.ImportPluginFromType<CampaignPlugin>("Campaign");
    kernel.ImportPluginFromType<EncounterPlugin>("Encounter");
    kernel.ImportPluginFromType<DicePlugin>("Dice");

    return kernel;
}
