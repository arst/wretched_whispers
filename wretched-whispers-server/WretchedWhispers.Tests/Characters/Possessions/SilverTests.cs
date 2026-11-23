using Moq;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions;

/// <summary>Silver used to move one way only: <c>BuyItem</c> took it and nothing gave it back, so a sale
/// or a looted purse could only be narrated.</summary>
public class SilverTests
{
    private static Character Hero() => TestCharacters.Create(new Dice(new Mock<IRandomService>().Object));

    private static InventoryItem Carry(Character hero, string description, int quantity = 1)
    {
        var item = new InventoryItem(Guid.NewGuid(), description, false, false, quantity);
        hero.AddItem(item);
        return item;
    }

    [Fact]
    public void GainSilver_AddsToSilver()
    {
        var hero = Hero();

        hero.GainSilver(25);

        Assert.Equal(35, hero.Silver);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GainSilver_RejectsNothingAndLess(int amount)
    {
        var hero = Hero();

        Assert.Throws<ArgumentOutOfRangeException>(() => hero.GainSilver(amount));
        Assert.Equal(10, hero.Silver);
    }

    [Fact]
    public void SellItem_RemovesTheUnitsAndPaysThePrice()
    {
        var hero = Hero();
        var torches = Carry(hero, "torches", quantity: 3);

        hero.SellItem(torches.Id, price: 6, quantity: 2);

        Assert.Equal(1, torches.Quantity);
        Assert.Equal(16, hero.Silver);
    }

    [Fact]
    public void SellItem_TheLastUnit_RemovesTheItem()
    {
        var hero = Hero();
        var crucifix = Carry(hero, "silver crucifix");

        hero.SellItem(crucifix.Id, price: 40);

        Assert.Empty(hero.Inventory.InventoryItems);
        Assert.Equal(50, hero.Silver);
    }

    [Fact]
    public void SellItem_ForNothing_IsAGift()
    {
        var hero = Hero();
        var rope = Carry(hero, "rope 30 ft");

        hero.SellItem(rope.Id, price: 0);

        Assert.Empty(hero.Inventory.InventoryItems);
        Assert.Equal(10, hero.Silver);
    }

    [Fact]
    public void SellItem_MoreThanCarried_ThrowsAndChangesNothing()
    {
        var hero = Hero();
        var torches = Carry(hero, "torches", quantity: 2);

        Assert.Throws<InvalidOperationException>(() => hero.SellItem(torches.Id, price: 30, quantity: 3));

        Assert.Equal(2, torches.Quantity);
        Assert.Equal(10, hero.Silver);
    }
}
