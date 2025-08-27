using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Challenge;

public class ChallengeOutcome
{
    private ChallengeOutcome(bool isSuccess, Natural natural)
    {
        IsSuccess = isSuccess;
        Natural = natural;
    }

    public bool IsSuccess { get; private set; }
    public Natural Natural { get; }

    public static ChallengeOutcome Success(Natural natural)
    {
        return new ChallengeOutcome(true, natural);
    }

    public static ChallengeOutcome Fail(Natural natural)
    {
        return new ChallengeOutcome(false, natural);
    }
}