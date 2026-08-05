"use client";

import type { StateUpdateEvent } from "@/types/api";

const INJURIES = [
  { key: "hasLostEye", glyph: "⊘", label: "LOST EYE" },
  { key: "hasStabbedLung", glyph: "☠", label: "STABBED LUNG" },
  { key: "hasBrokenHand", glyph: "✋", label: "BROKEN HAND" },
  { key: "hasCrushedFoot", glyph: "⬤", label: "CRUSHED FOOT" },
  { key: "hasSeveredArm", glyph: "✂", label: "SEVERED ARM" },
  { key: "hasSmashedFace", glyph: "✗", label: "SMASHED FACE" },
] as const;

export default function InjuryBadges({
  characterData,
}: {
  characterData: StateUpdateEvent;
}) {
  return (
    <div className="grid grid-cols-2 gap-2">
      {INJURIES.filter((i) => characterData[i.key]).map((injury) => (
        <div key={injury.key} className="flex items-center gap-1.5">
          <span className="text-[#ff1493] text-sm">{injury.glyph}</span>
          <span className="text-[#ff1493] text-xs font-bold uppercase tracking-wider">
            {injury.label}
          </span>
        </div>
      ))}
    </div>
  );
}
