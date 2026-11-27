using System.Collections.Frozen;
using System.ComponentModel;
using System.Reflection;
using WretchedWhispers.Engine.GameTools;

namespace WretchedWhispers.Engine.Services;

/// <summary>One game tool: the bound method, its model-facing name and description, its telemetry group,
/// the stages it may be called in, its per-turn call limit (0 = unlimited), and how it relates to the end
/// of a turn's game actions.</summary>
public sealed record GameToolDescriptor(
    string Name, string Group, Type DeclaringType, MethodInfo Method, IReadOnlyList<SessionStage> Stages,
    string Description, int MaxCallsPerTurn = 0, bool EndsTurn = false, bool AllowedAfterTurnEnd = false);

/// <summary>
/// Single source of truth for which game tools exist and in which stage each may be called, derived
/// once by reflecting <see cref="GameToolAttribute"/> over the *Tools classes. Every turn is offered
/// <see cref="All"/> the tools, so the list the model sees never changes with the stage and a turn
/// whose stage moves on (an aftermath closed, a fight won) can still reach the tools of the next one.
/// The stage allow-list is enforced when a tool is CALLED, against the stage at that moment (see
/// <see cref="AgentToolProvider"/>), and each description says when its tool can be called.
/// </summary>
public static class GameToolCatalog
{
    // The tool classes whose [GameTool] methods form the catalog. Adding a new tool class means
    // adding it here and constructing it in AgentToolProvider — both compile-checked, no strings.
    private static readonly Type[] ToolTypes =
    [
        typeof(CharacterTools),
        typeof(CampaignTools),
        typeof(EncounterTools),
        typeof(DiceTools)
    ];

    // Declared before All: static fields initialise in order, and Build reads this.
    private static readonly SessionStage[] PlayStages =
        [SessionStage.Exploration, SessionStage.Combat, SessionStage.Resolution];

    /// <summary>Every game tool, in declaration order.</summary>
    public static IReadOnlyList<GameToolDescriptor> All { get; } = Build();

    private static readonly FrozenDictionary<SessionStage, IReadOnlyList<GameToolDescriptor>> ByStage =
        Enum.GetValues<SessionStage>().ToFrozenDictionary(
            stage => stage,
            stage => (IReadOnlyList<GameToolDescriptor>)All.Where(d => d.Stages.Contains(stage)).ToList());

    /// <summary>The tools that may be called in <paramref name="stage"/>.</summary>
    public static IReadOnlyList<GameToolDescriptor> ForStage(SessionStage stage) => ByStage[stage];

    private static List<GameToolDescriptor> Build()
    {
        var all = new List<GameToolDescriptor>();

        foreach (var type in ToolTypes)
        {
            // Group is the class name without the "Tools" suffix (CharacterTools -> "Character"),
            // used only for telemetry/logging labels (the model sees the bare method name).
            var group = type.Name.Replace("Tools", string.Empty);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = method.GetCustomAttribute<GameToolAttribute>();
                if (attribute is null) continue;

                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                all.Add(new GameToolDescriptor(
                    method.Name, group, type, method, attribute.Stages,
                    (description + " " + WhenCallable(attribute.Stages)).Trim(),
                    attribute.MaxCallsPerTurn, attribute.EndsTurn, attribute.AllowedAfterTurnEnd));
            }
        }

        return all;
    }

    /// <summary>When a tool can be called, in words, so the model learns it from the description and not
    /// from a refusal. Nothing for a tool every stage of play allows.</summary>
    private static string WhenCallable(IReadOnlyList<SessionStage> stages)
    {
        var missing = PlayStages.Where(stage => !stages.Contains(stage)).ToList();
        if (missing.Count == 0) return "";
        return stages.Contains(SessionStage.Exploration)
            ? "Cannot be called " + string.Join(" or ", missing.Select(Phrase)) + "."
            : "Can be called only " + string.Join(" or ", stages.Select(Phrase)) + ".";
    }

    /// <summary>A stage in the words of a description or a refusal.</summary>
    internal static string Phrase(SessionStage stage) => stage switch
    {
        SessionStage.CampaignSetup => "before the campaign begins",
        SessionStage.Exploration => "outside a fight",
        SessionStage.Combat => "during a fight",
        SessionStage.Resolution => "in the aftermath of a fight",
        _ => stage.ToString()
    };
}
