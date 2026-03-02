using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Characters.Possessions.Scrolls;

public sealed class Scroll
{
    [JsonConstructor]
    public Scroll(Guid id, ScrollSchool school, string description)
    {
        Id = id;
        School = school;
        Description = description;
    }

    public Guid Id { get; }
    public ScrollSchool School { get; }
    public string Description { get; }
}