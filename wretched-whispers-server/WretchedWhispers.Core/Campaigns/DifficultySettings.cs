using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

/// <summary>The concrete knobs a difficulty level resolves to. Pure value object.</summary>
public sealed record DifficultySettings(
    int StartingHpBonus,
    DiceExpr MinorDamage,
    DiceExpr SeriousDamage,
    DiceExpr DeadlyDamage,
    DiceExpr DawnDice,
    string GmToneNote,
    // MORK BORG "Getting Better": whether a low ability roll (d6 < score) degrades the ability.
    // RAW says yes; StoryMode is improvement-only so forgiving campaigns don't regress.
    bool AbilityLossOnGettingBetter,
    // Encounter scaling. Nothing otherwise ties a GM-invented adversary to what the character can
    // actually do: a starting wretch swinging a d4 femur at 6 HP behind light armor (d2) needs ~18
    // rounds, because armor caps the RATE of damage, not just the total. Hit points scale and armor
    // is capped at creation so forgiving campaigns produce fights that end.
    ArmorTier MaxAdversaryArmor,
    double AdversaryHpScale,
    // Player-side only: a landed blow always draws at least 1. Without it, d4 vs d2 armor absorbs
    // roughly 4 in 9 successful hits to nothing, which reads as "I hit her and nothing happened".
    // Deliberately NOT applied to adversary retaliation — that would make forgiving tiers deadlier.
    bool PlayerHitsAlwaysDamage);
