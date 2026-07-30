using System.Text.Json.Serialization;
using WretchedWhispers.Core.Campaigns.Time;
using WretchedWhispers.Core.Campaigns.World;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

public sealed class Campaign
{
    [JsonConstructor]
    private Campaign(Guid id, string name, string description, int currentDay, int currentHour,
        List<Guid> characters, CalendarOfNechrubel calendar,
        DiceExpr dawnDice, List<Guid> encounters,
        bool isStarted = false, bool isEnded = false, bool isConfigured = false,
        List<JournalEntry>? journal = null, Difficulty difficulty = Difficulty.Grim,
        List<PointOfInterest>? pointsOfInterest = null, string? currentLocationName = null,
        List<FallenCharacter>? fallen = null)
    {
        Id = id;
        Name = name;
        Description = description;
        CurrentDay = currentDay;
        CurrentHour = currentHour;
        Characters = characters;
        Calendar = calendar;
        DawnDice = dawnDice;
        Encounters = encounters;
        IsStarted = isStarted;
        IsEnded = isEnded;
        IsConfigured = isConfigured;
        Journal = journal ?? [];
        Difficulty = difficulty;
        PointsOfInterest = pointsOfInterest ?? [];
        CurrentLocationName = currentLocationName;
        Fallen = fallen ?? [];
    }

    [JsonInclude] public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    [JsonInclude] public int CurrentDay { get; private set; }

    [JsonInclude] public int CurrentHour { get; private set; }

    [JsonInclude] internal CalendarOfNechrubel Calendar { get; }

    [JsonInclude] internal List<Guid> Characters { get; }

    [JsonInclude] internal DiceExpr DawnDice { get; private set; }

    [JsonInclude] internal List<Guid> Encounters { get; }

    [JsonInclude] internal bool IsStarted { get; private set; }

    [JsonInclude] public bool IsEnded { get; private set; }

    [JsonInclude] public bool IsConfigured { get; private set; }

    [JsonInclude] public Difficulty Difficulty { get; private set; }

    [JsonInclude] internal List<JournalEntry> Journal { get; }

    [JsonIgnore] public IReadOnlyList<JournalEntry> JournalEntries => Journal.AsReadOnly();

    [JsonInclude] internal List<PointOfInterest> PointsOfInterest { get; }

    [JsonIgnore] public IReadOnlyList<PointOfInterest> Pois => PointsOfInterest.AsReadOnly();

    [JsonInclude] public string? CurrentLocationName { get; private set; }

    [JsonInclude] internal List<FallenCharacter> Fallen { get; }

    [JsonIgnore] public IReadOnlyList<FallenCharacter> FallenCharacters => Fallen.AsReadOnly();

    [JsonIgnore] public bool WorldEnded => Calendar.WorldEnded;

    [JsonIgnore] public IReadOnlyCollection<Misery> Miseries => Calendar.Miseries;

    [JsonIgnore] public IReadOnlyCollection<Guid> EncounterIds => Encounters.AsReadOnly();

    [JsonIgnore] public IReadOnlyCollection<Guid> Players => Characters.AsReadOnly();

    internal AdvanceTimeOutcome AdvanceTime(int hours, Dice dice)
    {
        CurrentHour += hours;

        if (CurrentHour >= 24)
        {
            CurrentDay += CurrentHour / 24;
            CurrentHour %= 24;
            // Report only the misery this dawn actually triggered (0 or 1), not the standing tally —
            // otherwise every dawn would re-announce miseries the world already suffered.
            var triggered = Calendar.DawnRoll(DawnDice, dice);
            List<string> newMiseries = triggered is null ? [] : [triggered.Psalm];
            return new AdvanceTimeOutcome(newMiseries, Calendar.WorldEnded, true);
        }

        // No dawn crossed — no new misery, whatever the standing tally.
        return new AdvanceTimeOutcome([], Calendar.WorldEnded, false);
    }

    public void Configure(string name, string description)
    {
        if (IsStarted) throw new InvalidOperationException("Cannot configure a campaign that is already started.");

        Name = name;
        Description = description;
        IsConfigured = true;
    }

    public void JoinGame(Guid characterId)
    {
        Characters.Add(characterId);
    }

    public void AddEncounter(Guid encounterId)
    {
        Encounters.Add(encounterId);
    }

    public void RecordJournalEntry(JournalCategory category, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Journal text must not be empty.", nameof(text));
        Journal.Add(new JournalEntry(category, text.Trim(), CurrentDay, CurrentHour));
    }

    public void RecordPointOfInterest(PoiType type, string name, int x, int y, string? connectedTo = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Point of interest name must not be empty.", nameof(name));
        name = name.Trim();
        if (PointsOfInterest.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"'{name}' is already charted on the map.", nameof(name));

        string? canonicalConnection = null;
        if (connectedTo is not null)
        {
            var target = PointsOfInterest.FirstOrDefault(p =>
                p.Name.Equals(connectedTo.Trim(), StringComparison.OrdinalIgnoreCase));
            canonicalConnection = target?.Name
                ?? throw new ArgumentException($"'{connectedTo}' is not on the map.", nameof(connectedTo));
        }

        PointsOfInterest.Add(new PointOfInterest(
            name, type, Math.Clamp(x, 0, 100), Math.Clamp(y, 0, 100), canonicalConnection, CurrentDay));
    }

    public void SetPartyLocation(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Location name must not be empty.", nameof(name));
        var poi = PointsOfInterest.FirstOrDefault(p =>
            p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        CurrentLocationName = poi?.Name
            ?? throw new ArgumentException($"'{name}' is not on the map.", nameof(name));
    }

    public void BuryCharacter(Guid characterId, string name)
    {
        if (!Characters.Remove(characterId))
            throw new ArgumentException("Character is not part of this campaign.", nameof(characterId));
        Fallen.Add(new FallenCharacter(characterId, name, CurrentDay));
        RecordJournalEntry(JournalCategory.Event, $"Here fell {name}.");
    }

    public static Campaign Create(Difficulty difficulty, string name, string description)
    {
        var settings = DifficultyPresets.For(difficulty);
        return new Campaign(Guid.NewGuid(), name, description, 1, 0, [], new CalendarOfNechrubel(),
            settings.DawnDice, [], difficulty: difficulty);
    }

    public void Start()
    {
        if (IsStarted) throw new InvalidOperationException("Campaign is already started.");
        if (Players.Count == 0) throw new InvalidOperationException("Cannot start a campaign without players.");

        IsStarted = true;
    }

    public void End()
    {
        if (!IsStarted) throw new InvalidOperationException("Campaign is not started yet.");

        IsEnded = true;
    }

    public bool IsActive()
    {
        return IsStarted && !IsEnded;
    }
}