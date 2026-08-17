"use client";

import type { StateUpdateEvent } from "@/types/api";

const STATUSES = [
  {
    key: "isInfected",
    glyph: "☣",
    label: "INFECTED",
    color: "text-[#ff1493]",
    hint: "",
  },
  {
    key: "isDizzyFromMagic",
    glyph: "✨",
    label: "ARCANE DAZE",
    color: "text-[#ffe000]",
    hint: "",
  },
  {
    key: "isEncumbered",
    glyph: "⚓",
    label: "ENCUMBERED",
    color: "text-[#8a8a8a]",
    hint: "DR +2 on Strength and Agility — every swing and every dodge. Drop items to clear it.",
  },
] as const;

export default function StatusIndicators({
  characterData,
}: {
  characterData: StateUpdateEvent;
}) {
  return (
    <div className="space-y-1">
      {STATUSES.filter((s) => characterData[s.key]).map((status) => (
        <div key={status.key}>
          <div className="flex items-center gap-1.5">
            <span className={`${status.color} text-sm`}>{status.glyph}</span>
            <span
              className={`${status.color} text-xs font-bold tracking-wider uppercase`}
            >
              {status.label}
            </span>
          </div>
          {status.hint && (
            <p className="ml-5 text-[11px] leading-snug text-[#8a8a8a]">
              {status.hint}
            </p>
          )}
        </div>
      ))}
    </div>
  );
}
