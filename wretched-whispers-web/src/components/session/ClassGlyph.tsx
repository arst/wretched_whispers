import type { CharacterClass } from "@/types/api";

// Hand-drawn woodcut glyphs, one per class, monochrome via currentColor so the caller owns the
// colour and the selected/unselected states cost nothing. Deliberately not an icon dependency:
// nothing off the shelf has a "cursed skinwalker", and eight paths are smaller than a package.
// A null class is "let the dice decide" and gets the d20.
function Glyph({ characterClass }: { characterClass: CharacterClass | null }) {
  switch (characterClass) {
    // A maw with two fangs in it. Filled fangs, because outlined ones read as a "W" at 24px.
    case "FangedDeserter":
      return (
        <>
          <path d="M3 9C3 5 7 3 12 3s9 2 9 6c0 6-4.5 12-9 12S3 15 3 9Z" />
          <path d="M7.4 8 L9.2 15 L11 8 Z" fill="currentColor" stroke="none" />
          <path d="M13 8 L14.8 15 L16.6 8 Z" fill="currentColor" stroke="none" />
        </>
      );
    // A shiv: blade, crossguard, grip.
    case "GutterbornScum":
      return (
        <>
          <path d="M20 3.5 L11 12.5 L8.5 10 Z" />
          <path d="M6.5 12 L11.5 17" />
          <path d="M9 14.5 L4.5 19" />
        </>
      );
    // The eye that read too much.
    case "EsotericHermit":
      return (
        <>
          <path d="M2 12s4-5.5 10-5.5S22 12 22 12s-4 5.5-10 5.5S2 12 2 12Z" />
          <circle cx="12" cy="12" r="2.4" />
        </>
      );
    // A sprig pulled up by the root.
    case "OccultHerbmaster":
      return (
        <>
          <path d="M12 21.5 V5" />
          <path d="M12 12C8 12 6 10 6 7c3.5 0 6 2 6 5Z" />
          <path d="M12 9C16 9 18 7 18 4c-3.5 0-6 2-6 5Z" />
          <path d="M12 18C15.5 18 17 16.5 17 14c-3 0-5 1.5-5 4Z" />
        </>
      );
    // An inverted cross.
    case "HereticalPriest":
      return (
        <>
          <path d="M12 2.5 V21.5" />
          <path d="M6.5 16 H17.5" />
        </>
      );
    // Claw rakes.
    case "CursedSkinwalker":
      return (
        <>
          <path d="M5 4C7 9 8 15 7 20.5" />
          <path d="M11.5 3C13.5 8 14.5 14 13.5 20" />
          <path d="M18 4C20 9 20.5 15 19.5 20.5" />
        </>
      );
    // A bare skull.
    case "Classless":
      return (
        <>
          <path d="M12 2.5c4.7 0 7.5 3.2 7.5 7.3 0 2.6-1 4.2-2 5.2v3.5H6.5v-3.5c-1-1-2-2.6-2-5.2C4.5 5.7 7.3 2.5 12 2.5Z" />
          <circle cx="9.2" cy="10.5" r="1.5" fill="currentColor" stroke="none" />
          <circle cx="14.8" cy="10.5" r="1.5" fill="currentColor" stroke="none" />
          <path d="M12 13 L10.7 15.5 H13.3 Z" />
        </>
      );
    // The d20 the dice decide with.
    default:
      return (
        <>
          <path d="M12 2 L21 7 V17 L12 22 L3 17 V7 Z" />
          <path d="M12 6.5 L17.2 15.5 H6.8 Z" />
        </>
      );
  }
}

export default function ClassGlyph({
  characterClass,
  className,
}: {
  characterClass: CharacterClass | null;
  className?: string;
}) {
  return (
    <svg
      viewBox="0 0 24 24"
      aria-hidden="true"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinejoin="miter"
      className={className}
    >
      <Glyph characterClass={characterClass} />
    </svg>
  );
}
