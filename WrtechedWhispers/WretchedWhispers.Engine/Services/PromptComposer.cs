using WretchedWhispers.Engine.Prompts;
using WretchedWhispers.Core.Campaigns;

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

            ## Game State
            {snapshot}
            """;
    }
}
