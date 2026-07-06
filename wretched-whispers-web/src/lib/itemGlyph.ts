/**
 * Maps a freeform item description to a single monochrome glyph, extending the codebase's existing
 * glyph language (see InjuryBadges / EquipmentSlot). Keyword categories are tried in order (specific
 * before generic); the first whose keyword appears anywhere in the (lowercased) description wins.
 * Anything unmatched gets a neutral fallback rune, so every item still reads as "an item".
 *
 * This is a deliberately simple substring heuristic. Icons are purely cosmetic, so a rare mismatch
 * (e.g. an oddly-worded item landing on the fallback rune) is harmless — no correctness depends on it.
 */
const CATEGORIES: { glyph: string; keywords: string[] }[] = [
  { glyph: "⚕", keywords: ["medicine", "bandage", "salve", "herb", "remedy", "cure", "poultice", "ointment"] },
  { glyph: "⚱", keywords: ["potion", "vial", "flask", "elixir", "tonic", "draught", "philter", "oil", "ichor", "poison", "acid", "blood"] },
  { glyph: "⚷", keywords: ["key", "lockpick"] },
  { glyph: "✦", keywords: ["torch", "lantern", "candle", "lamp", "match", "tinder", "flint", "firewood"] },
  { glyph: "❧", keywords: ["scroll", "book", "tome", "grimoire", "parchment", "letter", "note", "map", "page", "paper", "codex"] },
  { glyph: "☠", keywords: ["skull", "bone", "relic", "idol", "fetish", "talisman", "amulet", "charm", "sigil", "totem", "corpse", "tooth", "finger"] },
  { glyph: "◉", keywords: ["coin", "silver", "gold", "money", "purse", "treasure", "gem", "jewel", "crown", "pearl", "diamond"] },
  { glyph: "❂", keywords: ["food", "ration", "bread", "meat", "meal", "provision", "waterskin", "water", "wine", "mead", "cheese", "fish", "fruit"] },
  { glyph: "⚙", keywords: ["tool", "kit", "instrument", "shovel", "spade", "pick", "saw", "gear", "rope", "chain"] },
  { glyph: "❖", keywords: ["armor", "armour", "shield", "helm", "mail", "plate", "buckler", "gambeson", "cloak", "robe", "boots", "gloves", "garb"] },
  { glyph: "†", keywords: ["sword", "blade", "dagger", "knife", "axe", "cleaver", "scimitar", "spear", "glaive", "scythe", "sabre", "saber", "staff", "club", "mace", "flail", "whip", "crossbow", "longbow", "sling", "hatchet", "rapier", "halberd", "warhammer", "cudgel", "wand", "rod", "weapon"] },
];

const FALLBACK = "◈";

export function itemGlyph(description: string): string {
  const text = description.toLowerCase();
  for (const { glyph, keywords } of CATEGORIES) {
    if (keywords.some((k) => text.includes(k))) return glyph;
  }
  return FALLBACK;
}
