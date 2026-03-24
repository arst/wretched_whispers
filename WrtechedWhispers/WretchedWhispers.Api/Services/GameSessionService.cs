using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Polly.Registry;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Semantic;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

namespace WretchedWhispers.Api.Services;

public sealed class GameSessionService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ICampaignsRepository campaignsRepository,
    IChatHistoryRepository chatHistoryRepository,
    ICharactersRepository charactersRepository,
    WretchedWhispersDbContext dbContext,
    ResiliencePipelineProvider<string> resilienceProvider)
{
    public async IAsyncEnumerable<SseEvent> ProcessAction(
        Guid sessionId,
        string playerMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Load campaign by sessionId (1:1 mapping)
        var campaign = await campaignsRepository.Get(sessionId);
        if (campaign is null)
        {
            yield return new SseEvent("error", new { message = "Session not found" });
            yield break;
        }

        // Get chat sessions for campaign, use the first session ID
        var chatSessions = await chatHistoryRepository.GetSessionsForCampaign(sessionId, ct);
        var chatSessionId = chatSessions.FirstOrDefault();
        if (chatSessionId == Guid.Empty)
        {
            yield return new SseEvent("error", new { message = "No chat session found for this campaign" });
            yield break;
        }

        // Channel bridge: producer writes events, consumer yields them
        var channel = Channel.CreateUnbounded<SseEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        // Fire-and-forget the agent turn -- writes events to channel
        _ = ExecuteAgentTurnAsync(sessionId, chatSessionId, playerMessage, channel.Writer, ct);

        // Yield events as they arrive from the channel
        await foreach (var sseEvent in channel.Reader.ReadAllAsync(ct))
        {
            yield return sseEvent;
        }
    }

    private async Task ExecuteAgentTurnAsync(
        Guid sessionId,
        Guid chatSessionId,
        string playerMessage,
        ChannelWriter<SseEvent> writer,
        CancellationToken ct)
    {
        try
        {
            // Load existing chat history
            var chatHistory = await chatHistoryRepository.LoadSession(chatSessionId, ct) ?? new ChatHistory();

            // Build Kernel per-turn
            var kernel = BuildKernelForSession();
            var agent = CreateGameMasterAgent(kernel);

            // Begin a database transaction for transactional buffering
            await dbContext.Database.BeginTransactionAsync(ct);

            try
            {
                // Add user message to chat history and persist
                await chatHistoryRepository.SaveMessage(
                    chatSessionId,
                    new ChatMessageContent(AuthorRole.User, playerMessage),
                    ct);

                // Wrap agent invocation in resilience pipeline
                var pipeline = resilienceProvider.GetPipeline("llm-retry");

                var fullResponseText = new System.Text.StringBuilder();
                var toolResults = new List<SseEvent>();

                await pipeline.ExecuteAsync(async token =>
                {
                    fullResponseText.Clear();
                    toolResults.Clear();

                    // Create thread and populate from loaded chat history
                    ChatHistoryAgentThread thread = new(chatHistory);

                    var userMessage = new ChatMessageContent(AuthorRole.User, playerMessage);

                    // Stream the agent response -- write each chunk to channel immediately
                    // Filter to only assistant-role content to avoid leaking tool call/result text
                    await foreach (var response in agent.InvokeStreamingAsync(userMessage, thread, cancellationToken: token))
                    {
                        if (response.Message.Role is not null && response.Message.Role != AuthorRole.Assistant)
                            continue;

                        var content = response.Message.Content;
                        if (!string.IsNullOrEmpty(content))
                        {
                            fullResponseText.Append(content);
                            writer.TryWrite(new SseEvent("narrative", new { text = content }));
                        }
                    }

                    // After streaming completes, read thread messages for tool results
                    await foreach (var completed in thread.GetMessagesAsync(token))
                    {
                        foreach (var item in completed.Items)
                        {
                            if (item is FunctionResultContent funcResult)
                            {
                                toolResults.Add(new SseEvent("tool_result", new
                                {
                                    function = funcResult.FunctionName,
                                    result = funcResult.Result
                                }));
                            }
                        }
                    }
                }, ct);

                // Write tool results after narrative
                foreach (var toolResult in toolResults)
                {
                    writer.TryWrite(toolResult);
                }

                // Save the full assistant response to chat history
                await chatHistoryRepository.SaveMessage(
                    chatSessionId,
                    new ChatMessageContent(AuthorRole.Assistant, fullResponseText.ToString())
                    {
                        AuthorName = "Game_Master"
                    },
                    ct);

                // Commit the database transaction
                await dbContext.Database.CommitTransactionAsync(ct);

                // Build state snapshot from current campaign + character state
                var updatedCampaign = await campaignsRepository.Get(sessionId);
                if (updatedCampaign is not null)
                {
                    var firstPlayerId = updatedCampaign.Players.FirstOrDefault();
                    int? characterHp = null;
                    int? characterMaxHp = null;
                    Guid? characterId = null;
                    string? characterName = null;
                    int? characterStrength = null;
                    int? characterAgility = null;
                    int? characterPresence = null;
                    int? characterToughness = null;
                    string? characterWeapon = null;
                    string? characterArmor = null;
                    string[]? characterInventory = null;

                    if (firstPlayerId != Guid.Empty)
                    {
                        var character = await charactersRepository.Get(firstPlayerId, ct);
                        if (character is not null)
                        {
                            characterId = character.Id;
                            characterHp = character.Hp.Current;
                            characterMaxHp = character.Hp.Max;
                            characterName = character.Name;
                            characterStrength = character.Abilities.Strength.Modifier;
                            characterAgility = character.Abilities.Agility.Modifier;
                            characterPresence = character.Abilities.Presence.Modifier;
                            characterToughness = character.Abilities.Toughness.Modifier;
                            characterWeapon = character.Weapon.Kind.ToString();
                            characterArmor = character.Armor.Tier switch
                            {
                                NoArmorTier => "None",
                                LightArmorTier => "Light Armor",
                                MediumArmorTier => "Medium Armor",
                                HeavyArmorTier => "Heavy Armor",
                                _ => "Unknown"
                            };
                            characterInventory = character.Inventory.InventoryItems
                                .Select(i => i.Description).ToArray();
                        }
                    }

                    writer.TryWrite(new SseEvent("state_update", new
                    {
                        campaignId = updatedCampaign.Id,
                        currentDay = updatedCampaign.CurrentDay,
                        currentHour = updatedCampaign.CurrentHour,
                        characterId,
                        characterName,
                        characterHp,
                        characterMaxHp,
                        characterStrength,
                        characterAgility,
                        characterPresence,
                        characterToughness,
                        characterWeapon,
                        characterArmor,
                        characterInventory,
                        miseryCount = updatedCampaign.Miseries.Count,
                        status = DeriveStatus(updatedCampaign)
                    }));
                }
            }
            catch (Exception ex)
            {
                // Rollback transaction on any failure -- game state not modified
                try
                {
                    await dbContext.Database.RollbackTransactionAsync();
                }
                catch
                {
                    // Rollback may fail if connection is already closed
                }

                writer.TryWrite(new SseEvent("error", new
                {
                    message = ex is OperationCanceledException
                        ? "Request was cancelled"
                        : "An error occurred while processing your action"
                }));
            }
        }
        catch
        {
            // Outer catch for errors before transaction starts (loading history, building kernel)
            writer.TryWrite(new SseEvent("error", new
            {
                message = "An error occurred while processing your action"
            }));
        }
        finally
        {
            // CRITICAL: Always complete the channel so the consumer loop exits
            writer.Complete();
        }
    }

    private Kernel BuildKernelForSession()
    {
        var kernelBuilder = Kernel.CreateBuilder();

        var deployment = configuration["AzureOpenAi:ChatModelDeployment"]!;
        var endpoint = configuration["AzureOpenAi:Endpoint"]!;
        var apiKey = configuration["AzureOpenAi:ApiKey"]!;

        kernelBuilder.AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey);

        var kernel = kernelBuilder.Build();

        // Import plugins from scoped container (DI-resolved instances)
        // This ensures plugins use the request-scoped DbContext
        var charPlugin = serviceProvider.GetRequiredService<CharacterPlugin>();
        var campaignPlugin = serviceProvider.GetRequiredService<CampaignPlugin>();
        var encounterPlugin = serviceProvider.GetRequiredService<EncounterPlugin>();
        var dicePlugin = serviceProvider.GetRequiredService<DicePlugin>();

        kernel.ImportPluginFromObject(charPlugin, "Character");
        kernel.ImportPluginFromObject(campaignPlugin, "Campaign");
        kernel.ImportPluginFromObject(encounterPlugin, "Encounter");
        kernel.ImportPluginFromObject(dicePlugin, "Dice");

        return kernel;
    }

    private static ChatCompletionAgent CreateGameMasterAgent(Kernel kernel)
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var summarizer = new ChatHistorySummarizationReducer(
            chatService,
            targetCount: 100,
            thresholdCount: 150)
        {
            SummarizationInstructions =
                """
                When summarizing this MORK BORG game session, preserve these critical elements:

                ESSENTIAL GAME STATE:
                - Character names, current hit points, abilities, scars, and omens
                - Current campaign location and time of day/season
                - Active encounters (adversaries, their status, ongoing combat)
                - Important NPCs the characters have met and their relationships
                - Key items, weapons, or artifacts in possession
                - Current goals, quests, or destinations
                - Recent significant events that affect the narrative

                PRESERVE THE ATMOSPHERE:
                - Maintain the doom-laden, apocalyptic tone of MORK BORG
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

                Format the summary as a narrative that maintains the MORK BORG tone while clearly stating the current game state.
                """
        };

        return new ChatCompletionAgent
        {
            Name = "Game_Master",
            HistoryReducer = summarizer,
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

                Session flow (follow this EXACT order — each step depends on the previous one):
                1. FIRST, create a character using CreateCharacter function. You MUST have the character ID before proceeding.
                2. THEN create a Campaign using CreateCampaign function. The campaign should reflect the doomed, collapsing world (examples: plague-ridden villages, ash-covered wastelands, decrepit cathedrals, drowning cities).
                3. THEN join the character to the campaign using AddCharacterToCampaign function. You need both the character ID from step 1 and the campaign ID from step 2.
                4. THEN start the campaign using StartCampaign function. The campaign must have at least one character added before it can start.
                5. Only after the campaign is started, begin by describing what happens: the player wakes in misery, filth, or strange omens. Always establish a grim and oppressive mood.
                6. If they meet someone dangerous or potentially dangerous, create an encounter using CreateEncounter function.
                7. Add adversaries to the encounter using AddAdversariesToEncounter function. Adversaries should feel grotesque and threatening, even if weak.
                8. Start the encounter using StartEncounter function.
                9. Describe the encounter and what happens: blood, pain, and broken things. Challenge the player or let them attack adversaries; adversaries attack without mercy.
                10. End the encounter using EndEncounter function when adversaries are no more (no active adversaries in the encounter).
                11. Generate results of the encounter. Lean into scars, broken bones, permanent consequences, or pyrrhic victories.
                12. Continue the game until the campaign ends in doom, despair, or some fleeting triumph against the inevitable.
                13. You can create more encounters, if/when players meet more adversaries.
                14. After each action that takes players some time (no less than 1 hour), advance campaign time using AdvanceTime function. Time matters: darkness falls, hunger gnaws, omens approach.

                Output rules:
                - NEVER output raw JSON, function results, IDs, or technical data to the player. The player must only see narrative prose.
                - When a tool returns data (character stats, campaign info, dice rolls), weave the results into your narration in-character. For example, instead of showing {"Name":"Test","Agility":-1}, say something like "Your wretched body is frail — barely able to swing a blade (Agility -1), though your stubborn will keeps you standing."
                - GUIDs, object structures, and function names must never appear in your text.

                Tone reminders:
                - Emphasize inevitability: the world ends soon, and everything the characters do is done against the ticking clock of apocalypse.
                - Nothing is clean or safe. Even victories carry wounds or curses.
                - Use vivid, visceral language. Describe smells, sounds, rot, blood, and ruin.
                - Players should feel both powerless and defiant -- doomed figures raging against the end of all things.
                """,
            Kernel = kernel,
            Arguments = new KernelArguments(
                new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
        };
    }

    private static string DeriveStatus(Campaign campaign)
    {
        if (campaign.Players.Count == 0)
            return "character-creation";
        if (campaign.IsActive())
            return "in-progress";
        return "ended";
    }
}
