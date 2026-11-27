using WretchedWhispers.Engine.Services;

namespace WretchedWhispers.Engine.GameTools;

/// <summary>
/// Marks a public method on a *Tools class as a game-master tool and declares the session stage(s)
/// in which the model may call it. Every tool is offered every turn; a call outside these stages is
/// refused when it is made, against the stage at that moment (see <see cref="GameToolCatalog"/>), so a
/// tool's name, description, and stage gating all live in one place: the method itself.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GameToolAttribute(params SessionStage[] stages) : Attribute
{
    public IReadOnlyList<SessionStage> Stages { get; } = stages;

    /// <summary>
    /// How many times the tool may complete successfully in one turn; 0 = unlimited. Enforced in code
    /// (see <see cref="Services.AgentToolProvider"/>), not just requested in the prompt: a further call
    /// fails with a model-readable error before it reaches the domain.
    /// </summary>
    public int MaxCallsPerTurn { get; init; }

    /// <summary>
    /// Completing this tool ends the turn's game actions: the session has moved to a stage whose own
    /// tools are not available until the player's next message (StartEncounter begins combat, but a
    /// combat round can only be resolved next turn). Any later tool call this turn fails with a
    /// model-readable error — except tools marked <see cref="AllowedAfterTurnEnd"/>.
    /// </summary>
    public bool EndsTurn { get; init; }

    /// <summary>Pure record-keeping (journal, map) that resolves nothing, so it may still follow a
    /// tool that <see cref="EndsTurn"/>.</summary>
    public bool AllowedAfterTurnEnd { get; init; }
}
