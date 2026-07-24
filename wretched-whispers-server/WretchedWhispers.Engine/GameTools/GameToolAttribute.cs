using WretchedWhispers.Engine.Services;

namespace WretchedWhispers.Engine.GameTools;

/// <summary>
/// Marks a public method on a *Tools class as a game-master tool and declares the session stage(s)
/// in which the model may call it. The stage → tool allow-list (formerly the hand-maintained
/// <c>StageToolMap</c>) is DERIVED from these attributes by <see cref="GameToolCatalog"/>, so a
/// tool's name, description, and stage gating all live in one place: the method itself. There is no
/// separate stringly-typed list to drift out of sync.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GameToolAttribute(params SessionStage[] stages) : Attribute
{
    public IReadOnlyList<SessionStage> Stages { get; } = stages;
}
