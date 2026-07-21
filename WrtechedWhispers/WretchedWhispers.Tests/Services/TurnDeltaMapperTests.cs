using WretchedWhispers.Engine.Models;
using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class TurnDeltaMapperTests
{
    [Fact]
    public void NarratedPurchaseThatCalledNoTool_DiffsToZeroSilverAndNoItems()
    {
        // The fabrication case: the GM narrated "you spend 4 silver, the map is yours" but called no
        // BuyItem, so the committed state is unchanged before vs. after. The delta must expose that as a
        // no-op — this is the authoritative account that contradicts the invented prose.
        var before = Living();
        var after = Living(); // identical: nothing was actually applied

        var delta = TurnDeltaMapper.Compute(before, after);

        Assert.Equal(0, delta.SilverChange);
        Assert.Empty(delta.ItemsAdded);
        Assert.Empty(delta.ItemsRemoved);
        Assert.True(delta.IsNoOp);
    }

    [Fact]
    public void RealPurchase_ReportsSilverSpentAndItemGained()
    {
        var before = Living() with { CharacterSilver = 120, CharacterInventory = [] };
        var after = Living() with { CharacterSilver = 116, CharacterInventory = ["Tattered map fragment"] };

        var delta = TurnDeltaMapper.Compute(before, after);

        Assert.Equal(-4, delta.SilverChange);
        Assert.Equal(["Tattered map fragment"], delta.ItemsAdded);
        Assert.Empty(delta.ItemsRemoved);
        Assert.False(delta.IsNoOp);
    }

    [Fact]
    public void MultisetInventory_TracksDuplicateAddAndRemove()
    {
        var before = Living() with { CharacterInventory = ["Torch", "Rope"] };
        var after = Living() with { CharacterInventory = ["Torch", "Torch"] };

        var delta = TurnDeltaMapper.Compute(before, after);

        Assert.Equal(["Torch"], delta.ItemsAdded);   // gained a second torch
        Assert.Equal(["Rope"], delta.ItemsRemoved);  // spent the rope
    }

    [Fact]
    public void HpTimeAndAfflictions_ReportedFromFlips()
    {
        var before = Living() with { CharacterHp = 8, CurrentDay = 1, CurrentHour = 6, IsInfected = false };
        var after = Living() with { CharacterHp = 3, CurrentDay = 1, CurrentHour = 12, IsInfected = true };

        var delta = TurnDeltaMapper.Compute(before, after);

        Assert.Equal(-5, delta.HpChange);
        Assert.Equal(6, delta.HoursElapsed);
        Assert.Contains("Infected", delta.NewAfflictions);
        Assert.False(delta.IsNoOp);
    }

    [Fact]
    public void DeathAcrossMidnight_ReportsDiedAndPositiveHoursOnly()
    {
        var before = Living() with { CurrentDay = 1, CurrentHour = 20, IsDead = false };
        var after = Living() with { CurrentDay = 2, CurrentHour = 2, IsDead = true };

        var delta = TurnDeltaMapper.Compute(before, after);

        Assert.True(delta.Died);
        Assert.Equal(6, delta.HoursElapsed); // day*24+hour arithmetic crosses midnight correctly
    }

    // A living character with silver, full HP, empty inventory, no afflictions. `with` overrides per test.
    private static StateUpdate Living() => new(
        CampaignId: Guid.NewGuid(),
        CurrentDay: 1,
        CurrentHour: 0,
        CharacterId: Guid.NewGuid(),
        CharacterName: "Tuck",
        CharacterHp: 8,
        CharacterMaxHp: 8,
        CharacterStrength: 0,
        CharacterAgility: 0,
        CharacterPresence: 0,
        CharacterToughness: 0,
        CharacterWeapon: "Staff",
        CharacterArmor: "Medium",
        CharacterInventory: [],
        CharacterSilver: 120,
        MiseryCount: 0,
        Stage: "exploration",
        Status: "in-progress",
        HasLostEye: false,
        HasStabbedLung: false,
        HasBrokenHand: false,
        HasCrushedFoot: false,
        HasSeveredArm: false,
        HasSmashedFace: false,
        IsInfected: false,
        IsDizzyFromMagic: false,
        IsEncumbered: false,
        IsDead: false,
        ArmorTier: "medium",
        HasShield: false,
        IsShieldBroken: false,
        WorldEnded: false,
        CurrentLocationName: null,
        CharacterOmens: 0,
        CharacterScrolls: [],
        MiseryPsalms: []);
}
