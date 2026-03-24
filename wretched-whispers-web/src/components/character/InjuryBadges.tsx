"use client";

import type { CharacterData } from "@/types/api";

interface InjuryBadgesProps {
  characterData: CharacterData;
}

const INJURIES = [
  { key: "hasLostEye" as const, glyph: "\u2298", label: "LOST EYE" },
  { key: "hasStabbedLung" as const, glyph: "\u2620", label: "STABBED LUNG" },
  { key: "hasBrokenHand" as const, glyph: "\u270B", label: "BROKEN HAND" },
  { key: "hasCrushedFoot" as const, glyph: "\u2B24", label: "CRUSHED FOOT" },
  { key: "hasSeveredArm" as const, glyph: "\u2702", label: "SEVERED ARM" },
  { key: "hasSmashedFace" as const, glyph: "\u2717", label: "SMASHED FACE" },
];

export default function InjuryBadges({ characterData }: InjuryBadgesProps) {
  const activeInjuries = INJURIES.filter((i) => characterData[i.key]);

  if (activeInjuries.length === 0) return null;

  return (
    <>
      <span className="text-xs font-bold uppercase text-[#8a8a8a]">
        INJURIES
      </span>
      <div className="grid grid-cols-2 gap-2 mt-2">
        {activeInjuries.map((injury) => (
          <div key={injury.key} className="flex items-center gap-1.5">
            <span className="text-[#ff1493] text-sm">{injury.glyph}</span>
            <span className="text-[#ff1493] text-xs font-bold uppercase tracking-wider">
              {injury.label}
            </span>
          </div>
        ))}
      </div>
    </>
  );
}
