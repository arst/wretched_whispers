using Moq;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Tests;

/// <summary>
///     Base class for unit tests that provides a Dice instance backed by a mocked random service.
///     NOTE: <see cref="IRandomService.GenerateRandomRoll"/> is 0-based — a queued value of 3 on a d6
///     surfaces as a roll of 4.
/// </summary>
public abstract class TestBase
{
    protected TestBase()
    {
        MockRandomService = new Mock<IRandomService>();
        Dice = new Dice(MockRandomService.Object);
    }

    protected Mock<IRandomService> MockRandomService { get; }
    protected Dice Dice { get; }

    /// <summary>
    ///     Queues 0-based roll values returned in sequence. Throws once the queue is exhausted —
    ///     falling back to real randomness would make the test silently nondeterministic.
    /// </summary>
    protected void SetupDiceRolls(params int[] rolls)
    {
        var queue = new Queue<int>(rolls);
        MockRandomService.Setup(x => x.GenerateRandomRoll(It.IsAny<int>()))
            .Returns((int sides) => queue.Count > 0
                ? queue.Dequeue()
                : throw new InvalidOperationException(
                    $"Dice script exhausted: production rolled more than the {rolls.Length} scripted value(s). "
                    + "Add the missing rolls to SetupDiceRolls."));
    }

    /// <summary>
    ///     Returns a fixed 0-based value for every roll of a die with <paramref name="diceSides"/> sides
    ///     (so a <paramref name="zeroBasedValue"/> of 0 is a roll of 1).
    /// </summary>
    protected void SetupDiceRoll(int diceSides, int zeroBasedValue)
    {
        MockRandomService.Setup(x => x.GenerateRandomRoll(diceSides))
            .Returns(zeroBasedValue);
    }
}
