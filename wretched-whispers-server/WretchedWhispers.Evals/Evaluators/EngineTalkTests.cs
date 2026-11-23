using Xunit;

namespace WretchedWhispers.Evals.Evaluators;

public class EngineTalkTests
{
    // Each of these was shown to a player.
    [Theory]
    [InlineData("What do you do? (Remember: after StartEncounter the next message resolves the first round of combat.)", "StartEncounter")]
    [InlineData("I will call ChallengeCharacter when the stakes are real.", "ChallengeCharacter")]
    [InlineData("The domain has arranged the bones — combat begins.", "the domain")]
    [InlineData("damage taken 2, and the domain reports the worst — isDead true, current HP 0.", "the domain")]
    [InlineData("damage taken 2, and the domain reports the worst — isDead true, current HP 0.", "isDead")]
    [InlineData("Your numbers, straight from Game State: HP 2/2.", "Game State")]
    [InlineData("(Retaliation breakdown: roll 10, modifier 0, DR 14; baseDamageRoll 4, armorReduction 3 → 1 damage.)", "baseDamageRoll")]
    public void Find_ReportsEngineVocabulary(string narration, string expected)
    {
        Assert.Contains(expected, EngineTalk.Find(narration), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    // "Rest" and "Roll" are tool names and also plain English.
    [InlineData("Rest here a while. Roll your shoulders and listen to the thunder roll.")]
    [InlineData("You rolled 8 + 2 = 10 against DR 12. You fail, and take 2 damage. HP 0/2.")]
    [InlineData("You have crossed into the domain of the Shadow King.")]
    [InlineData("If you climb, that will take an Agility test. What do you do?")]
    public void Find_LeavesTableTalkAndFictionAlone(string narration)
    {
        Assert.Empty(EngineTalk.Find(narration));
    }
}
