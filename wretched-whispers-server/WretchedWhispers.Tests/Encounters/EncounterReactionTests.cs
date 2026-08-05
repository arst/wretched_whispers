using System.Text.Json;
using Xunit;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure.Persistence.Serialization;

namespace WretchedWhispers.Tests.Encounters;

public sealed class EncounterReactionTests : TestBase
{
    private readonly JsonSerializerOptions _options = AggregateJsonOptions.Create();

    private static Adversary MinimalAdversary() => new(
        "Goblin", new HitPoints(5, 5), new Armor(ArmorTier.Light), 7,
        new AttackProfile("Claw", DiceExpr.D4));

    // Mock is 0-based per die: SetupDiceRolls(a, b) -> 2d6 total a + b + 2.
    [Theory]
    [InlineData(0, 0, 2, InitialReaction.Kill, EncounterType.Hostile)]
    [InlineData(1, 0, 3, InitialReaction.Kill, EncounterType.Hostile)]
    [InlineData(1, 1, 4, InitialReaction.Angered, EncounterType.Hostile)]
    [InlineData(2, 2, 6, InitialReaction.Angered, EncounterType.Hostile)]
    [InlineData(3, 2, 7, InitialReaction.Indifferent, EncounterType.Friendly)]
    [InlineData(3, 3, 8, InitialReaction.Indifferent, EncounterType.Friendly)]
    [InlineData(4, 3, 9, InitialReaction.AlmostFriendly, EncounterType.Friendly)]
    [InlineData(4, 4, 10, InitialReaction.AlmostFriendly, EncounterType.Friendly)]
    [InlineData(5, 4, 11, InitialReaction.Helpful, EncounterType.Friendly)]
    [InlineData(5, 5, 12, InitialReaction.Helpful, EncounterType.Friendly)]
    public void UnknownCreation_RollsAndStoresReaction(
        int die1, int die2, int expectedRoll, InitialReaction expectedReaction, EncounterType expectedType)
    {
        SetupDiceRolls(die1, die2);

        var encounter = Encounter.Create("Strangers", "Figures in the fog", EncounterType.Unknown, Dice);

        Assert.Equal(expectedRoll, encounter.ReactionRoll);
        Assert.Equal(expectedReaction, encounter.Reaction);
        Assert.Equal(expectedType, encounter.CurrentType);
    }

    [Fact]
    public void DeclaredHostile_SetsCurrentTypeHostile_NoReaction()
    {
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);

        Assert.Equal(EncounterType.Hostile, encounter.CurrentType);
        Assert.Null(encounter.Reaction);
        Assert.Null(encounter.ReactionRoll);
    }

    [Fact]
    public void DeclaredFriendly_SetsCurrentTypeFriendly_NoReaction()
    {
        var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, Dice);

        Assert.Equal(EncounterType.Friendly, encounter.CurrentType);
        Assert.Null(encounter.Reaction);
        Assert.Null(encounter.ReactionRoll);
    }

    [Fact]
    public void StartEncounter_WhileFriendly_Throws()
    {
        var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, Dice);
        encounter.AddAdversary(MinimalAdversary());

        Assert.Throws<InvalidOperationException>(() => encounter.StartEncounter());
    }

    [Fact]
    public void TurnHostile_ThenStartEncounter_Succeeds()
    {
        var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, Dice);
        encounter.AddAdversary(MinimalAdversary());

        encounter.TurnHostile();
        encounter.StartEncounter();

        Assert.Equal(EncounterType.Hostile, encounter.CurrentType);
        Assert.True(encounter.IsStarted);
    }

    [Fact]
    public void TurnHostile_WhenAlreadyHostileOrStarted_IsIdempotent()
    {
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);
        encounter.AddAdversary(MinimalAdversary());
        encounter.StartEncounter();

        encounter.TurnHostile();

        Assert.Equal(EncounterType.Hostile, encounter.CurrentType);
        Assert.True(encounter.IsStarted);
    }

    [Fact]
    public void TurnHostile_OnEndedEncounter_Throws()
    {
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);
        var adversary = MinimalAdversary();
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();
        adversary.ReceiveDamage(5);
        encounter.EndEncounter();

        Assert.Throws<InvalidOperationException>(() => encounter.TurnHostile());
    }

    [Fact]
    public void Reaction_RoundTripsThroughJson()
    {
        SetupDiceRolls(3, 2); // 2d6 = 7 -> Indifferent -> Friendly
        var encounter = Encounter.Create("Strangers", "Figures in the fog", EncounterType.Unknown, Dice);

        var json = JsonSerializer.Serialize(encounter, _options);
        var restored = JsonSerializer.Deserialize<Encounter>(json, _options);

        Assert.NotNull(restored);
        Assert.Equal(InitialReaction.Indifferent, restored.Reaction);
        Assert.Equal(7, restored.ReactionRoll);
        Assert.Equal(EncounterType.Friendly, restored.CurrentType);
    }

    [Fact]
    public void Encounter_DeserializesFromBlobWithoutReactionFields()
    {
        // Backward compat: blobs persisted before reaction storage must still load.
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);
        var json = JsonSerializer.Serialize(encounter, _options);
        using var doc = JsonDocument.Parse(json);
        var stripped = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            if (prop.Name != "reaction" && prop.Name != "reactionRoll")
                stripped[prop.Name] = prop.Value;
        var legacyJson = JsonSerializer.Serialize(stripped);

        var restored = JsonSerializer.Deserialize<Encounter>(legacyJson, _options);

        Assert.NotNull(restored);
        Assert.Null(restored.Reaction);
        Assert.Null(restored.ReactionRoll);
    }

    // MORK BORG RAW: 2d6 strictly HIGHER than morale and they break. Mock is 0-based per die, so
    // SetupDiceRolls(a, b) -> 2d6 total a + b + 2. Morale is 7 for MinimalAdversary.
    [Theory]
    [InlineData(0, 0, false)] //  2 — holds
    [InlineData(2, 3, false)] //  7 — equal to morale, holds
    [InlineData(3, 3, true)]  //  8 — over morale, breaks
    [InlineData(5, 5, true)]  // 12 — routs
    public void MoraleCheck_BreaksOnlyWhenRollExceedsMorale(int die1, int die2, bool expectedFled)
    {
        // 5 max HP, 4 damage -> 1 left, which is at-or-below a third: the check fires.
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);
        encounter.AddAdversary(MinimalAdversary());
        encounter.StartEncounter();
        var adversary = encounter.Adversaries.Single();
        // Drop it under the 30%-HP threshold that triggers the check for a lone adversary: a 4-damage
        // hit on 5 max HP leaves 1. First scripted roll is the morale 2d6 (the hit is passed in, not rolled).
        SetupDiceRolls(die1, die2);

        encounter.ProcessPlayerAttackOutcome(
            new AttackOutcome(Hit: true, Damage.From(4), Critical: false, Fumble: false,
                WeaponBroken: false, TargetArmorDegraded: false),
            adversary.Id, Dice);

        Assert.Equal(expectedFled, adversary.IsFled);
    }

    [Fact]
    public void MoraleCheck_SmallAdversary_CanStillBreakWhileAlive()
    {
        // Difficulty scaling shrinks hit points, so the threshold has to stay reachable at 3 HP.
        // The old float form (< Max * 0.3) needed under 0.9 HP here — i.e. only a corpse qualified.
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);
        encounter.AddAdversary(new Adversary(
            "Rat", new HitPoints(3, 3), new Armor(ArmorTier.None), 5,
            new AttackProfile("teeth", DiceExpr.D4)));
        encounter.StartEncounter();
        var adversary = encounter.Adversaries.Single();
        SetupDiceRolls(5, 5); // 2d6 = 12, well over morale 5

        encounter.ProcessPlayerAttackOutcome(
            new AttackOutcome(Hit: true, Damage.From(2), Critical: false, Fumble: false,
                WeaponBroken: false, TargetArmorDegraded: false),
            adversary.Id, Dice);

        Assert.Equal(1, adversary.Hp.Current);
        Assert.False(adversary.IsDead);
        Assert.True(adversary.IsFled);
    }
}
