namespace WretchedWhispers.Core.Characters.Status.Broken;

public abstract record BrokenOutcome(string Kind)
{
    public static BrokenOutcome BrokenHand()
    {
        return new BrokenHand();
    }
    
    public static BrokenOutcome CrushedFoot()
    {
        return new CrushedFoot();
    }
    
    public static BrokenOutcome SeveredArm()
    {
        return new SeveredArm();
    }

    public static BrokenOutcome SmashedFace()
    {
        return new SmashedFace();
    }
    
    public static BrokenOutcome StabbedLung()
    {
        return new StabbedLung();
    }

    public static BrokenOutcome EyeLost()
    {
        return new EyeLost();
    }

    public static BrokenOutcome Dead()
    {
        return new DeadBroken();
    }
}