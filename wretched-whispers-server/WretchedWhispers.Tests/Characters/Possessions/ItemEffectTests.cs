using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Challenge;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions;

/// <summary>Using an item used to do one thing: remove it. A life elixir was drunk and healed nothing.
/// An item now carries what it does, and using it applies that.</summary>
public class ItemEffectTests : TestBase
{
    private Character WoundedHero(int wounds)
    {
        var hero = TestCharacters.Create(Dice, maxHp: 20);
        var grim = DifficultyPresets.For(Difficulty.Grim);
        for (var i = 0; i < wounds; i++) hero.SufferConsequence(ChallengeConsequence.Minor, grim, Dice);
        return hero;
    }

    private static InventoryItem Carry(Character hero, string description, ItemEffect effect, int quantity = 1)
    {
        var item = new InventoryItem(Guid.NewGuid(), description, false, true, quantity, effect);
        hero.AddItem(item);
        return item;
    }

    [Fact]
    public void UseItem_HealingItem_RestoresHpAndCuresInfection()
    {
        // d4 daily powers; three wounds of 2; d6 = 4 healed.
        SetupDiceRolls(0, 1, 1, 1, 3);
        var hero = WoundedHero(wounds: 3);
        hero.Infect();
        var elixir = Carry(hero, "life elixir", ItemEffect.Heal, quantity: 2);

        var outcome = hero.UseItem(elixir.Id, Dice);

        Assert.Equal(4, outcome.HpHealed);
        Assert.True(outcome.InfectionCured);
        Assert.Equal(18, hero.Hp.Current);
        Assert.False(hero.IsInfected);
        Assert.Equal(1, elixir.Quantity);
    }

    [Fact]
    public void UseItem_HealingItem_NeverHealsPastTheMaximum()
    {
        // d4 daily powers; one wound of 2; d6 = 6 rolled, 2 healed.
        SetupDiceRolls(0, 1, 5);
        var hero = WoundedHero(wounds: 1);
        var elixir = Carry(hero, "life elixir", ItemEffect.Heal);

        var outcome = hero.UseItem(elixir.Id, Dice);

        Assert.Equal(2, outcome.HpHealed);
        Assert.False(outcome.InfectionCured);
        Assert.Equal(20, hero.Hp.Current);
    }

    [Fact]
    public void UseItem_OrdinaryItem_HealsNothingAndRollsNothing()
    {
        // d4 daily powers; one wound of 2. A further roll would exhaust the script and throw.
        SetupDiceRolls(0, 1);
        var hero = WoundedHero(wounds: 1);
        var torch = Carry(hero, "torches", ItemEffect.None, quantity: 2);

        var outcome = hero.UseItem(torch.Id, Dice);

        Assert.Equal(0, outcome.HpHealed);
        Assert.Equal(18, hero.Hp.Current);
        Assert.Equal(1, torch.Quantity);
    }

    [Fact]
    public void UseItem_TheLastUnit_RemovesTheItem()
    {
        SetupDiceRolls(0, 3);
        var hero = WoundedHero(wounds: 0);
        var elixir = Carry(hero, "life elixir", ItemEffect.Heal);

        hero.UseItem(elixir.Id, Dice);

        Assert.Empty(hero.Inventory.InventoryItems);
    }
}
