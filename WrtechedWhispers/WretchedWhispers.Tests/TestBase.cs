using Moq;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Tests;

/// <summary>
///     Base class for all unit tests that provides proper Dice initialization with a mocked random service.
/// </summary>
public abstract class TestBase : IDisposable
{
    protected TestBase()
    {
        // Create a mock random service for predictable test results
        MockRandomService = new Mock<IRandomService>();

        // Initialize the Dice static class with our mock
        Dice.SetRandomGenerator(MockRandomService.Object);
    }

    private Mock<IRandomService> MockRandomService { get; }

    public virtual void Dispose()
    {
        // Clean up any test-specific setup
        MockRandomService.Reset();
    }

    /// <summary>
    ///     Sets up the mock random service to return predictable dice roll results.
    /// </summary>
    /// <param name="rolls">Array of roll results to return in sequence</param>
    protected void SetupDiceRolls(params int[] rolls)
    {
        var queue = new Queue<int>(rolls);
        MockRandomService.Setup(x => x.GenerateRandomRoll(It.IsAny<int>()))
            .Returns((int sides) => queue.Count > 0 ? queue.Dequeue() : Random.Shared.Next(1, sides));
    }

    /// <summary>
    ///     Sets up the mock random service to return a specific result for dice of a particular size.
    /// </summary>
    /// <param name="diceSides">The number of sides on the die</param>
    /// <param name="result">The result to return (0-based, so result 0 = roll of 1)</param>
    protected void SetupDiceRoll(int diceSides, int result)
    {
        MockRandomService.Setup(x => x.GenerateRandomRoll(diceSides))
            .Returns(result);
    }
}