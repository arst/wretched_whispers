namespace WretchedWhispers.Core.Dice;

public readonly record struct DiceExpr(int Count, int Sides, int Constant = 0)
{
    public static DiceExpr d2 => new(1, 2);
    public static DiceExpr d4 => new(1, 4);
    public static DiceExpr d6 => new(1, 6);
    public static DiceExpr d8 => new(1, 8);
    public static DiceExpr d10 => new(1, 10);
    public static DiceExpr d12 => new(1, 12);
    public static DiceExpr d20 => new(1, 20);

    public static DiceExpr operator +(DiceExpr e, int k)
    {
        return new DiceExpr(e.Count, e.Sides, e.Constant + k);
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
                if (!int.TryParse(constantPart.Substring(1), out constant))
                    throw new ArgumentException($"Invalid constant modifier: {constantPart}", nameof(diceExpression));
            }
            else if (constantPart.StartsWith('-'))
            {
                if (!int.TryParse(constantPart.Substring(1), out constant))
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

// -----------------------------
// Shared Kernel / Value Objects
// -----------------------------

// -----------------------------
// Domain: Scrolls / Powers
// -----------------------------

// -----------------------------
// Domain: Combat
// -----------------------------

// -----------------------------
// Domain: Calendar of Nechrubel (Miseries)
// -----------------------------

// -----------------------------
// Entities / Aggregate Roots
// -----------------------------

// -----------------------------
// Domain Services: Reaction & Morale
// -----------------------------

// -----------------------------
// Policies & Guards
// -----------------------------

// -----------------------------
// Repositories (ports)
// -----------------------------

// -----------------------------
// In-Memory / Test helpers for dev
// -----------------------------

// -----------------------------
// Example usage (for tests or a console app) — not part of domain
// -----------------------------
/*
var rng = new SeededRandomService(42);
var abilities = new Abilities(new(+1), new(+0), new(+2), new(+1));
var hero = new Character("Grittr", abilities, Weapon.Create(WeaponKind.Sword), new Armor(ArmorTier.Light), new Shield(), rng, startingOmens: 1);
hero.NewDawn(rng);

// Encounter: attack a goblin in no armor
var goblinArmor = new Armor(ArmorTier.None);
var attack = hero.Attack(rng, AttackKind.Melee, goblinArmor);
if (attack.Hit)
{
    // apply damage to goblin ...
}

// Defend against incoming attack
var defence = hero.Defend(rng);
var incoming = new Damage(6);
hero.ReceiveDamage(rng, incoming, defence.FumbleDoubleDamage);

// Cast a scroll (if any known)
hero.LearnScroll(new Scroll(ScrollSchool.Unclean, key: "death"));
var cast = hero.Cast(rng, hero.KnownScrolls.First());
*/