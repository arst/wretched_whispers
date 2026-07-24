using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Engine.GameTools;

/// <summary>
/// Validation guards for model-supplied tool arguments. Messages are written for the MODEL to read:
/// when a guard throws, Agent Framework feeds the message back (IncludeDetailedErrors) so the model
/// can fix its call and retry, instead of the bad argument throwing deep in the domain and failing
/// the whole turn.
/// </summary>
internal static class ToolGuard
{
    public static void Quantity(int value, string name)
    {
        if (value < 1)
            throw new ArgumentException($"{name} must be at least 1.", name);
    }

    public static void NonNegative(int value, string name)
    {
        if (value < 0)
            throw new ArgumentException($"{name} must be 0 or greater.", name);
    }

    public static void Positive(int value, string name, string hint)
    {
        if (value <= 0)
            throw new ArgumentException($"{name} must be a positive number ({hint}).", name);
    }

    public static void Negative(int value, string name, string hint)
    {
        if (value >= 0)
            throw new ArgumentException($"{name} must be a negative number ({hint}).", name);
    }

    public static void InRange(int value, int min, int max, string name, string hint)
    {
        if (value < min || value > max)
            throw new ArgumentException($"{name} must be between {min} and {max} ({hint}).", name);
    }

    public static void DiceExpression(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !TryParseDice(value))
            throw new ArgumentException(
                $"{name} must look like 'd6', '1d8', '1d6+2', or '2d10-3'.", name);
    }

    private static bool TryParseDice(string value)
    {
        try
        {
            _ = DiceExpr.Parse(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
