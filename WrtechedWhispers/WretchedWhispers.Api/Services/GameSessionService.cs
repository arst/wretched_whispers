using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Polly.Registry;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Plugins.CombatAgent;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Api.Plugins.GameMasterPlugins.Adapters;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
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
    IEncountersRepository encountersRepository,
    WretchedWhispersDbContext dbContext,
    ResiliencePipelineProvider<string> resilienceProvider,
    StagePluginRegistry stagePluginRegistry,
    PromptComposer promptComposer)
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

            // Build SessionContext from loaded state
            var sessionContext = await BuildSessionContextAsync(sessionId, ct);

            // Build Kernel per-turn with wrapper plugins
            var kernel = BuildKernelForSession(sessionContext);
            var stage = sessionContext.DeriveStage();

            // Begin a database transaction for transactional buffering
            await dbContext.Database.BeginTransactionAsync(ct);

            try
            {
                // Add user message to chat history and persist
                await chatHistoryRepository.SaveMessage(
                    chatSessionId,
                    new ChatMessageContent(AuthorRole.User, playerMessage),
                    ct);

                var fullResponseText = new System.Text.StringBuilder();

                if (stage == SessionStage.Combat)
                {
                    // Combat sub-agent resolves the encounter (D-02)
                    var combatService = new CombatAgentService();
                    var combatNarrative = await combatService.ResolveCombat(
                        sessionContext, kernel, stagePluginRegistry, writer, ct);

                    fullResponseText.Append(combatNarrative);
                }
                else
                {
                    // Regular game master agent flow
                    var agent = CreateGameMasterAgent(kernel, sessionContext);

                    // Wrap agent invocation in resilience pipeline
                    var pipeline = resilienceProvider.GetPipeline("llm-retry");
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

                // Reload state after turn to capture mutations made during the turn
                var postTurnContext = await BuildSessionContextAsync(sessionId, ct);

                // Build state snapshot from current campaign + character state
                var updatedCampaign = postTurnContext.Campaign;
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
                    bool hasLostEye = false;
                    bool hasStabbedLung = false;
                    bool hasBrokenHand = false;
                    bool hasCrushedFoot = false;
                    bool hasSeveredArm = false;
                    bool hasSmashedFace = false;
                    bool isInfected = false;
                    bool isDizzyFromMagic = false;
                    bool isEncumbered = false;
                    bool isDead = false;
                    string armorTier = "none";
                    bool hasShield = false;
                    bool isShieldBroken = false;

                    if (firstPlayerId != Guid.Empty)
                    {
                        var character = postTurnContext.Character;
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
                            hasLostEye = character.HasLostEye;
                            hasStabbedLung = character.HasStabbedLung;
                            hasBrokenHand = character.HasBrokenHand;
                            hasCrushedFoot = character.HasCrushedFoot;
                            hasSeveredArm = character.HasSeveredArm;
                            hasSmashedFace = character.HasSmashedFace;
                            isInfected = character.IsInfected;
                            isDizzyFromMagic = character.IsDizzyFromMagic;
                            isEncumbered = character.IsEncumbered;
                            isDead = character.IsDead;
                            armorTier = character.Armor.Tier switch
                            {
                                NoArmorTier => "none",
                                LightArmorTier => "light",
                                MediumArmorTier => "medium",
                                HeavyArmorTier => "heavy",
                                _ => "none"
                            };
                            hasShield = character.Shield is not null;
                            isShieldBroken = character.Shield?.IsBroken ?? false;
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
                        stage = postTurnContext.DeriveStage().ToString().ToLowerInvariant(),
                        status = DeriveStatus(updatedCampaign),
                        hasLostEye,
                        hasStabbedLung,
                        hasBrokenHand,
                        hasCrushedFoot,
                        hasSeveredArm,
                        hasSmashedFace,
                        isInfected,
                        isDizzyFromMagic,
                        isEncumbered,
                        isDead,
                        armorTier,
                        hasShield,
                        isShieldBroken,
                        worldEnded = updatedCampaign.WorldEnded
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

    private async Task<SessionContext> BuildSessionContextAsync(Guid sessionId, CancellationToken ct)
    {
        var sessionContext = new SessionContext { SessionId = sessionId };

        // Load campaign
        var campaign = await campaignsRepository.Get(sessionId);
        if (campaign is not null)
        {
            sessionContext.Campaign = campaign;
            sessionContext.SetCampaignId(campaign.Id);

            // Load first character if exists
            var firstPlayerId = campaign.Players.FirstOrDefault();
            if (firstPlayerId != Guid.Empty)
            {
                var character = await charactersRepository.Get(firstPlayerId, ct);
                if (character is not null)
                {
                    sessionContext.Character = character;
                    sessionContext.SetCharacterId(character.Id);
                }
            }

            // Load active encounter (last non-resolved encounter)
            foreach (var encId in campaign.EncounterIds.Reverse())
            {
                var enc = await encountersRepository.Get(encId);
                if (enc is not null && enc.IsStarted && !enc.IsResolved)
                {
                    sessionContext.ActiveEncounter = enc;
                    sessionContext.SetActiveEncounterId(enc.Id);
                    break;
                }
            }
        }

        return sessionContext;
    }

    private Kernel BuildKernelForSession(SessionContext sessionContext)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        var settings = new Settings();
        var deployment = settings.AzureOpenAi.ChatModelDeployment;
        var endpoint = settings.AzureOpenAi.Endpoint;
        var apiKey = settings.AzureOpenAi.ApiKey;

        kernelBuilder.AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey);

        var kernel = kernelBuilder.Build();

        // Import WRAPPER plugins instead of original plugins (per D-10)
        // Resolve original plugins from DI and wrap them via adapters
        var charWrapper = new CharacterWrapperPlugin(
            new CharacterPluginAdapter(serviceProvider.GetRequiredService<CharacterPlugin>()),
            sessionContext, campaignsRepository);
        var campaignWrapper = new CampaignWrapperPlugin(
            new CampaignPluginAdapter(serviceProvider.GetRequiredService<CampaignPlugin>()),
            campaignsRepository, sessionContext);
        var encounterWrapper = new EncounterWrapperPlugin(
            new EncounterPluginAdapter(serviceProvider.GetRequiredService<EncounterPlugin>()), sessionContext);
        var diceWrapper = new DiceWrapperPlugin(
            new DicePluginAdapter(serviceProvider.GetRequiredService<DicePlugin>()));
        var resolutionWrapper = new ResolutionWrapperPlugin(sessionContext, encountersRepository);

        kernel.ImportPluginFromObject(charWrapper, "Character");
        kernel.ImportPluginFromObject(campaignWrapper, "Campaign");
        kernel.ImportPluginFromObject(encounterWrapper, "Encounter");
        kernel.ImportPluginFromObject(diceWrapper, "Dice");
        kernel.ImportPluginFromObject(resolutionWrapper, "Resolution");

        // Register StageTransitionFilter — stage is LOCKED at turn start, never re-derived mid-turn
        var stage = sessionContext.DeriveStage();
        var allowedFunctions = stagePluginRegistry.GetFunctionsForStage(stage, kernel);
        kernel.AutoFunctionInvocationFilters.Add(new StageTransitionFilter(stage, allowedFunctions));

        return kernel;
    }

    private ChatCompletionAgent CreateGameMasterAgent(
        Kernel kernel, SessionContext sessionContext)
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

        var stage = sessionContext.DeriveStage();
        var allowedFunctions = stagePluginRegistry.GetFunctionsForStage(stage, kernel);

        return new ChatCompletionAgent
        {
            Name = "Game_Master",
            HistoryReducer = summarizer,
            Instructions = promptComposer.Compose(sessionContext),
            Kernel = kernel,
            Arguments = new KernelArguments(
                new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                        functions: allowedFunctions)
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
