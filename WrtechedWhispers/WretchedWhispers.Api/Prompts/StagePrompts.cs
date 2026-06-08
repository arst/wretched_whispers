using WretchedWhispers.Api.Services;

namespace WretchedWhispers.Api.Prompts;

public static class StagePrompts
{
    public static string For(SessionStage stage) => stage switch
    {
        SessionStage.CharacterCreation => CharacterCreation,
        SessionStage.CampaignSetup => CampaignSetup,
        SessionStage.Exploration => Exploration,
        SessionStage.Combat => Combat,
        SessionStage.Resolution => Resolution,
        SessionStage.Ended => Ended,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    private const string CharacterCreation = """
        You are beginning a new session. The player's message is their character's name.
        IMMEDIATELY call CreateCharacter with that name -- do not ask for the name again.
        As you narrate, describe the character's wretched origins as their stats are generated --
        their scars, their pitiful belongings, their dim hope. Make the player feel the weight
        of their doomed existence from the very first breath.
        """;

    private const string CampaignSetup = """
        A character has been created. Now establish the campaign:
        1. Configure the campaign -- give it a doom-appropriate name and description, and set the dawn roll pace
           (d100 very slow to d2 the end is nigh). Ask the player which pace they want.
        2. Start the campaign.
        The character is already part of the campaign. Narrate the world they enter, the doom that awaits,
        the first omens. Use separate tool calls for each step to give narrative space between beats.
        """;

    private const string Exploration = """
        The campaign is underway. The character wanders a dying world.
        - Describe environments: rotting, rusted, broken, corrupted.
        - When the character attempts ANY risky action, ALWAYS call ChallengeCharacter to test against a DR (usually 12). Never narrate success or failure without rolling.
        - When violence or combat begins, IMMEDIATELY call CreateEncounter to set up the fight, AddAdversaryToEncounter to add enemies, then StartEncounter to begin combat. Do NOT narrate combat without creating an encounter first.
        - Call AdvanceTime after meaningful actions (no less than 1 hour). Time matters: darkness falls, hunger gnaws, omens approach.
        - The player can buy items, cast scrolls, rest, or explore.
        - Never ask the player to "roll" — YOU call the tools to resolve actions mechanically, then narrate the results.
        """;

    private const string Combat = """
        Combat is underway. Resolve EXACTLY ONE round from the player's message, then STOP and wait
        for the player's next action. Do NOT resolve the whole fight in a single turn — the player
        acts every round.

        This round, in order:
        1. Resolve the PLAYER's stated action first. If they attack, call AttackAdversary with the
           target's name. For other actions (cast a scroll, flee, use an item), call the matching tool.
        2. Then the enemies strike back: call AttackPlayer once for each living adversary.
        3. Narrate ONLY what the tool calls actually returned — real hits, misses, damage, and deaths.
           NEVER invent a hit, a wound, or a death that a tool did not report. Call the tool, then
           describe its result.
        4. If, after this round, all adversaries are dead or fled, call EndEncounter.

        Combat is brutal and fast in MORK BORG. Resolve only this single exchange, then return control
        to the player.
        """;

    private const string Resolution = """
        Combat has ended. Handle the aftermath:
        - Narrate the consequences: wounds, broken weapons, scattered loot.
        - Distribute loot by adding items to character inventory.
        - Apply injuries, infections, or ability degradation as warranted.
        - Cure infections or improve abilities if the narrative justifies it.
        - Advance time for rest or recovery.
        - When the aftermath is complete, call CompleteResolution to return to exploration.
        """;

    private const string Ended = """
        The session has ended -- either through character death or the 7th Misery destroying the world.
        Deliver a final narration: a eulogy for the fallen, a description of the world's last moments,
        or a bitter reflection on the futility of defiance. Make it memorable and haunting.
        Do not call any tools -- simply narrate the end.
        """;
}
