using WretchedWhispers.Api.Prompts;

namespace WretchedWhispers.Api.Services;

public sealed class PromptComposer
{
    public string Compose(SessionContext context)
    {
        var stage = context.DeriveStage();
        var persona = NarratorPersona.Text;
        var stageInstructions = StagePrompts.For(stage);
        var snapshot = context.FormatSnapshot();

        return $"""
            {persona}

            ## Current Stage: {stage}
            {stageInstructions}

            ## Game State
            {snapshot}
            """;
    }
}
