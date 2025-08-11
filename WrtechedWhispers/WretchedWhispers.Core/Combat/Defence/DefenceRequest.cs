using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Combat.Defence;

public sealed class DefenceRequest
{
    public DefenceRequest(Dr baseDr = default)
    {
        BaseDr = baseDr.Value == 0 ? new Dr(12) : baseDr;
    }

    public Dr BaseDr { get; }
}