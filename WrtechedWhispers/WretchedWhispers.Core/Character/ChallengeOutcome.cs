namespace WretchedWhispers.Core.Character;

public class ChallengeOutcome
{
    public bool IsSuccess { get; private set; }

    private ChallengeOutcome(bool isSuccess)
    {
        IsSuccess = isSuccess;
    }
    
    public static ChallengeOutcome Success()
    {
        return new ChallengeOutcome(true);
    }
    
    public static ChallengeOutcome Fail()
    {
        return new ChallengeOutcome(false);
    }
}