using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Possessions;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions;

public class InventoryTests
{
    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var inventory = new Inventory("Test Container", 5, []);

        // Assert
        Assert.Equal("Test Container", inventory.Container);
        Assert.Equal(5, inventory.MaxCapacity);
        Assert.Empty(inventory.InventoryItems);
        Assert.Equal(5, inventory.GetFreeSlots());
    }

    [Fact]
    public void IsFull_WhenInventoryAtCapacity_ShouldReturnTrue()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        for (var i = 0; i < 10; i++)
        {
            inventory.AddItem(new InventoryItem(Guid.NewGuid(), $"Item {i}", false, false));
        }

        // Act & Assert
        Assert.True(inventory.IsFull);
    }

    [Fact]
    public void AddItem_WhenInventoryHasSpace_ShouldAddItem()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var normalItem = new InventoryItem(Guid.NewGuid(), "Rope", false, false);

        // Act
        inventory.AddItem(normalItem);

        // Assert
        Assert.Contains(normalItem, inventory.InventoryItems);
        Assert.Equal(9, inventory.GetFreeSlots());
    }

    [Fact]
    public void AddItem_WhenInventoryIsFull_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var normalItem = new InventoryItem(Guid.NewGuid(), "Rope", false, false);
        for (int i = 0; i < 10; i++)
        {
            inventory.AddItem(new InventoryItem(Guid.NewGuid(), $"Item {i}", false, false));
        }

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => inventory.AddItem(normalItem));
    }

    [Fact]
    public void AddItem_BulkyItemWithOneFreeSlot_ShouldThrowInsteadOfOverfilling()
    {
        var inventory = new Inventory("Backpack", 3, []);
        inventory.AddItem(new InventoryItem(Guid.NewGuid(), "Rope", false, false));
        inventory.AddItem(new InventoryItem(Guid.NewGuid(), "Torch", false, false));

        Assert.Throws<InvalidOperationException>(() =>
            inventory.AddItem(new InventoryItem(Guid.NewGuid(), "Heavy Armor", true, false)));
    }

    [Fact]
    public void IsFull_WhenCapacityIsBelowOccupancy_ShouldStayFull()
    {
        // A Strength loss shrinks MaxCapacity below what's already carried (Character.ModifyAbility);
        // this is that state as it comes back from persistence. The inventory must report full, not
        // resurrect as it did when free slots went negative.
        var inventory = new Inventory("Backpack", 3, [
            new InventoryItem(Guid.NewGuid(), "Heavy Armor", true, false),
            new InventoryItem(Guid.NewGuid(), "Heavy Shield", true, false)]);

        Assert.True(inventory.IsFull);
        Assert.Throws<InvalidOperationException>(() =>
            inventory.AddItem(new InventoryItem(Guid.NewGuid(), "Rope", false, false)));
    }

    [Fact]
    public void AddItem_BulkyItem_ShouldTakeTwoSlots()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var bulkyItem = new InventoryItem(Guid.NewGuid(), "Heavy Armor", true, false);

        // Act
        inventory.AddItem(bulkyItem);

        // Assert
        Assert.Contains(bulkyItem, inventory.InventoryItems);
        Assert.Equal(8, inventory.GetFreeSlots());
    }

    [Fact]
    public void RemoveItem_WhenItemExists_ShouldRemoveItem()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var normalItem = new InventoryItem(Guid.NewGuid(), "Rope", false, false);
        inventory.AddItem(normalItem);

        // Act
        inventory.RemoveItem(normalItem.Id);

        // Assert
        Assert.DoesNotContain(normalItem, inventory.InventoryItems);
        Assert.Equal(10, inventory.GetFreeSlots());
    }

    [Fact]
    public void RemoveItem_WhenItemDoesNotExist_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => inventory.RemoveItem(nonExistentId));
    }

    [Fact]
    public void ConsumeItem_WhenItemExistsAndHasQuantity_ShouldConsumeOne()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var consumableItem = new InventoryItem(Guid.NewGuid(), "Health Potion", false, true, 3);
        inventory.AddItem(consumableItem);
        var initialQuantity = consumableItem.Quantity;

        // Act
        var result = inventory.ConsumeItem(consumableItem.Id);

        // Assert
        Assert.True(result);
        Assert.Equal(initialQuantity - 1, consumableItem.Quantity);
        Assert.Contains(consumableItem, inventory.InventoryItems);
    }

    [Fact]
    public void ConsumeItem_WhenItemQuantityReachesZero_ShouldRemoveItem()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var singleUseItem = new InventoryItem(Guid.NewGuid(), "Single Use Item", false, true);
        inventory.AddItem(singleUseItem);

        // Act
        var result = inventory.ConsumeItem(singleUseItem.Id);

        // Assert
        Assert.True(result);
        Assert.DoesNotContain(singleUseItem, inventory.InventoryItems);
    }

    [Fact]
    public void ConsumeItem_WhenItemHasZeroQuantity_ShouldReturnFalse()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var emptyItem = new InventoryItem(Guid.NewGuid(), "Empty Item", false, true, 0);
        inventory.AddItem(emptyItem);

        // Act
        var result = inventory.ConsumeItem(emptyItem.Id);

        // Assert
        Assert.False(result);
        Assert.Contains(emptyItem, inventory.InventoryItems);
    }

    [Fact]
    public void ConsumeItem_WhenItemDoesNotExist_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => inventory.ConsumeItem(nonExistentId));
    }

    [Fact]
    public void ReplenishItem_WhenItemExists_ShouldIncreaseQuantity()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var consumableItem = new InventoryItem(Guid.NewGuid(), "Health Potion", false, true, 3);
        inventory.AddItem(consumableItem);
        var initialQuantity = consumableItem.Quantity;

        // Act
        inventory.ReplenishItem(consumableItem.Id, 2);

        // Assert
        Assert.Equal(initialQuantity + 2, consumableItem.Quantity);
    }

    [Fact]
    public void ReplenishItem_WhenItemDoesNotExist_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => inventory.ReplenishItem(nonExistentId));
    }

    [Fact]
    public void GetFreeSlots_WithMixedItems_ShouldCalculateCorrectly()
    {
        // Arrange
        var inventory = new Inventory("Backpack", 10, []);
        var normalItem = new InventoryItem(Guid.NewGuid(), "Rope", false, false);
        var bulkyItem = new InventoryItem(Guid.NewGuid(), "Heavy Armor", true, false);
        var consumableItem = new InventoryItem(Guid.NewGuid(), "Health Potion", false, true, 3);

        inventory.AddItem(normalItem); // 1 slot
        inventory.AddItem(bulkyItem);  // 2 slots
        inventory.AddItem(consumableItem); // 1 slot

        // Act
        var freeSlots = inventory.GetFreeSlots();

        // Assert
        Assert.Equal(6, freeSlots); // 10 - 4 = 6
    }

    [Theory]
    [InlineData(-3, 4, false)] // Strength -3 + 8 = 5 free, occupied 4: under the allowance
    [InlineData(-3, 5, false)] // exactly Strength+8 is still carried free
    [InlineData(-3, 6, true)]  // one over the allowance
    [InlineData(0, 8, false)]  // Strength 0 + 8 = 8 free, occupied 8: exact, free
    [InlineData(0, 9, true)]   // one over
    [InlineData(2, 10, false)] // Strength 2 + 8 = 10 free, occupied 10: exact, free
    [InlineData(2, 11, true)]  // one over
    public void IsEncumbered_WithVariousStrengthAndOccupiedSlots_ShouldCalculateCorrectly(
        int strengthModifier, int occupiedSlots, bool expectedEncumbered)
    {
        // Arrange
        var inventory = new Inventory("Backpack", 20, []);
        var strength = new AbilityScore(strengthModifier);

        // Add items to reach desired occupied slots
        var slotsToAdd = occupiedSlots;
        while (slotsToAdd > 0)
        {
            if (slotsToAdd >= 2)
            {
                inventory.AddItem(new InventoryItem(Guid.NewGuid(), "Bulky Item", true, false));
                slotsToAdd -= 2;
            }
            else
            {
                inventory.AddItem(new InventoryItem(Guid.NewGuid(), "Normal Item", false, false));
                slotsToAdd -= 1;
            }
        }

        // Act
        var isEncumbered = inventory.IsEncumbered(strength);

        // Assert
        Assert.Equal(expectedEncumbered, isEncumbered);
    }
}
