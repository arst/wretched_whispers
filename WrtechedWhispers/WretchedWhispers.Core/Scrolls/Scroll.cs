namespace WretchedWhispers.Core.Scrolls;

public sealed class Scroll
{
    public Scroll(ScrollSchool school, string key)
    {
        School = school;
        Key = key;
    }

    public ScrollSchool School { get; }
    
    public string Key { get; } // identifier such as "death", "levitation", etc. Implementation left open.
}