using System.Collections.Frozen;
using System.Reflection;
using WretchedWhispers.Api.GameTools;

namespace WretchedWhispers.Api.Services;

/// <summary>One game tool: the bound method, its model-facing name, and its telemetry group.</summary>
public sealed record GameToolDescriptor(string Name, string Group, Type DeclaringType, MethodInfo Method);

/// <summary>
/// Single source of truth for which game tools exist and in which stage each is exposed. Built once
/// by reflecting <see cref="GameToolAttribute"/> over the *Tools classes — replacing the
/// hand-maintained StageToolMap string lists and the per-turn reflection-by-name that used to live in
/// <see cref="AgentToolProvider"/>. The stage allow-list is still the hard guardrail: only the tools
/// listed for a stage are ever built into that turn's agent, so out-of-stage actions are impossible.
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

    private static readonly FrozenDictionary<SessionStage, IReadOnlyList<GameToolDescriptor>> ByStage = Build();

    public static IReadOnlyList<GameToolDescriptor> ForStage(SessionStage stage) =>
        ByStage.TryGetValue(stage, out var tools) ? tools : [];

    private static FrozenDictionary<SessionStage, IReadOnlyList<GameToolDescriptor>> Build()
    {
        var byStage = new Dictionary<SessionStage, List<GameToolDescriptor>>();

        foreach (var type in ToolTypes)
        {
            // Group is the class name without the "Tools" suffix (CharacterTools -> "Character"),
            // used only for telemetry/logging labels (the model sees the bare method name).
            var group = type.Name.Replace("Tools", string.Empty);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = method.GetCustomAttribute<GameToolAttribute>();
                if (attribute is null) continue;

                var descriptor = new GameToolDescriptor(method.Name, group, type, method);
                foreach (var stage in attribute.Stages)
                {
                    if (!byStage.TryGetValue(stage, out var list))
                        byStage[stage] = list = [];
                    list.Add(descriptor);
                }
            }
        }

        return byStage.ToFrozenDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<GameToolDescriptor>)kv.Value);
    }
}
