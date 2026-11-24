using System.Security.Cryptography;
using System.Text;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Engine.Prompts;

namespace WretchedWhispers.Engine.Services;

public sealed class PromptComposer
{
    public string Compose(SessionContext context) =>
        $"""
            {ComposeInstructions(context)}
            ## Game State
            {context.FormatSnapshot()}
            """;

    /// <summary>Identifies the prompt this turn ran under, for telling prompt versions apart across runs.
    /// It covers the instructions only: Game State changes every turn, so a hash that included it would
    /// differ on almost every turn and identify nothing. Two turns in the same stage, difficulty and class
    /// share a hash for as long as the prompt text is unchanged.</summary>
    public string VersionHash(SessionContext context) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ComposeInstructions(context))));

    /// <summary>Everything the narrator is told apart from Game State.</summary>
    private static string ComposeInstructions(SessionContext context)
    {
        var stage = context.DeriveStage();
        var toneNote = DifficultyPresets.For(context.Campaign?.Difficulty ?? Difficulty.Grim).GmToneNote;

        return $"""
            {NarratorPersona.Text}

            ## Current Stage: {stage}
            {StagePrompts.For(stage)}

            ## Difficulty
            {toneNote}
            {ComposeClassSection(context)}
            {NarratorPersona.ClosingReminder}

            """;
    }

    /// <summary>The narrative half of a class: what the domain does not compute. Classless wretches resolve
    /// to an empty note and get no section at all, which keeps prompts for pre-class characters unchanged.
    /// </summary>
    private static string ComposeClassSection(SessionContext context)
    {
        if (context.Character is null) return "";

        var note = ClassPresets.For(context.Character.Class).NarratorNote;
        if (string.IsNullOrWhiteSpace(note)) return "";

        return $"""

            ## Class
            {note}
            Everything above is colour and judgement, never arithmetic. The character's real numbers, gear and
            abilities are in Game State and come from tools -- a class NEVER grants an item, a heal, a bonus or
            a success on its own.

            """;
    }
}
