using System.Text.RegularExpressions;
using WretchedWhispers.Engine.Services;

namespace WretchedWhispers.Evals.Evaluators;

/// <summary>
/// Exact check for engine vocabulary in player-facing narration: words that exist only in the
/// prompt, the tool list and tool results, so finding one in the story is never a matter of taste.
/// Deliberately narrow — the judgement calls (notes to self, restated instructions) belong to the
/// LLM judge; this catches the unambiguous cases for free and names them in the failure message.
/// </summary>
public static partial class EngineTalk
{
    // Compound names only: "Rest" and "Roll" are tools too, and ordinary words in any story.
    private static readonly Regex ToolNames = new(
        @"\b(" + string.Join("|", Enum.GetValues<SessionStage>()
            .SelectMany(GameToolCatalog.ForStage)
            .Select(tool => tool.Name)
            .Where(name => name.Skip(1).Any(char.IsUpper))
            .Distinct()) + @")\b",
        RegexOptions.Compiled);

    // "the domain of the Shadow King" is fiction; "the domain reports" is the prompt talking.
    [GeneratedRegex(@"\bthe domain\b(?!s? of\b)|\bgame state\b|\bengine record\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnginePhrases();

    // camelCase is how tool results name their fields (isDead, baseDamageRoll), and never how prose is spelled.
    [GeneratedRegex(@"\b[a-z]+[A-Z][A-Za-z]*\b")]
    private static partial Regex FieldNames();

    /// <summary>The distinct engine terms found in the narration, in order of appearance.</summary>
    public static IReadOnlyList<string> Find(string narration) =>
        ToolNames.Matches(narration)
            .Concat(EnginePhrases().Matches(narration))
            .Concat(FieldNames().Matches(narration))
            .OrderBy(match => match.Index)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
