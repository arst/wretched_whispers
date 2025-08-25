namespace WretchedWhispers.Core.Characters.Challenge;

public class ChallengeOutcome
{
    private ChallengeOutcome(bool isSuccess)
    {
        IsSuccess = isSuccess;
    }

    public bool IsSuccess { get; private set; }

    public static ChallengeOutcome Success()
    {
        return new ChallengeOutcome(true);
    }

    public static ChallengeOutcome Fail()
    {
        return new ChallengeOutcome(false);
    }
}