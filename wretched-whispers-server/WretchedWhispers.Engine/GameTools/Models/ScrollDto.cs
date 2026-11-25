using System.ComponentModel;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;

namespace WretchedWhispers.Engine.GameTools.Models;

public class ScrollDto
{
    [Description("School of magic the scroll belongs to")]
    public ScrollSchool School { get; set; }

    [Description("Unique key identifying the specific scroll")]
    public string Key { get; set; } = string.Empty;
}

/// <summary>A scroll that came into the character's hands in play: the name CastScroll knows it by and
/// what its power does.</summary>
public sealed record FoundScrollDto(string Name, ScrollSchool School, string? Effect);
