namespace WretchedWhispers.Core.Dices;

public readonly record struct DiceExpr(int Count, int Sides, int Constant = 0)
{
    public static DiceExpr Zero => new(0, 0);
    public static DiceExpr D2 => new(1, 2);
    public static DiceExpr D3 => new(1, 3);
    public static DiceExpr D4 => new(1, 4);
    public static DiceExpr D6 => new(1, 6);
    public static DiceExpr D8 => new(1, 8);
    public static DiceExpr D10 => new(1, 10);
    public static DiceExpr D12 => new(1, 12);
    public static DiceExpr D20 => new(1, 20);

    public static DiceExpr operator +(DiceExpr e, int k)
    {
        return e with { Constant = e.Constant + k };
    }

    public static DiceExpr D(int count, int sides, int k = 0)
    {
        return new DiceExpr(count, sides, k);
    }

    public static DiceExpr Parse(string diceExpression)
    {
        if (string.IsNullOrWhiteSpace(diceExpression))
            throw new ArgumentException("Dice expression cannot be null or empty", nameof(diceExpression));
        var expr = diceExpression.Trim().ToLowerInvariant();

        var constant = 0;
        var dicePart = expr;

        var lastPlusIndex = expr.LastIndexOf('+');
        var lastMinusIndex = expr.LastIndexOf('-');
        var splitIndex = Math.Max(lastPlusIndex, lastMinusIndex);

        if (splitIndex > 0)
        {
            dicePart = expr.Substring(0, splitIndex);
            var constantPart = expr.Substring(splitIndex);

            if (constantPart.StartsWith('+'))
            {
                if (!int.TryParse(constantPart.AsSpan(1), out constant))
                    throw new ArgumentException($"Invalid constant modifier: {constantPart}", nameof(diceExpression));
            }
            else if (constantPart.StartsWith('-'))
            {
                if (!int.TryParse(constantPart.AsSpan(1), out constant))
                    throw new ArgumentException($"Invalid constant modifier: {constantPart}", nameof(diceExpression));
                constant = -constant;
            }
        }

        if (!dicePart.Contains('d'))
            throw new ArgumentException($"Invalid dice expression format: {diceExpression}", nameof(diceExpression));

        var parts = dicePart.Split('d');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid dice expression format: {diceExpression}", nameof(diceExpression));

        int count;
        if (string.IsNullOrEmpty(parts[0]))
            count = 1;
        else if (!int.TryParse(parts[0], out count) || count <= 0)
            throw new ArgumentException($"Invalid dice count: {parts[0]}", nameof(diceExpression));

        if (!int.TryParse(parts[1], out var sides) || sides <= 0)
            throw new ArgumentException($"Invalid dice sides: {parts[1]}", nameof(diceExpression));

        return new DiceExpr(count, sides, constant);
    }
}