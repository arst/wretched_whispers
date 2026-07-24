namespace WretchedWhispers.Core.Campaigns;

public enum JournalCategory { Npc, Location, Promise, Quest, Event }

/// <summary>An append-only fact in the campaign's fiction, stamped with campaign time. The GM's
/// durable memory: what is not written here is forgotten when chat history is summarized.</summary>
public sealed record JournalEntry(JournalCategory Category, string Text, int Day, int Hour);
