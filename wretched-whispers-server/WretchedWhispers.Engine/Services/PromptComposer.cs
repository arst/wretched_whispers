using WretchedWhispers.Engine.Prompts;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Classes;

namespace WretchedWhispers.Engine.Services;

public sealed class PromptComposer
{
    public string Compose(SessionContext context)
    {
        var stage = context.DeriveStage();
        var persona = NarratorPersona.Text;
        var stageInstructions = StagePrompts.For(stage);
        var snapshot = context.FormatSnapshot();
        var toneNote = DifficultyPresets.For(context.Campaign?.Difficulty ?? Difficulty.Grim).GmToneNote;

        return $"""
            {persona}

            ## Current Stage: {stage}
            {stageInstructions}

            ## Difficulty
            {toneNote}
            {ComposeClassSection(context)}
            ## Game State
            {snapshot}
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
