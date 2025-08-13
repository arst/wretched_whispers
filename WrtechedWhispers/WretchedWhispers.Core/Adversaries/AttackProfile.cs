using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Adversaries;

public readonly record struct AttackProfile(string Description, DiceExpr DamageDie);