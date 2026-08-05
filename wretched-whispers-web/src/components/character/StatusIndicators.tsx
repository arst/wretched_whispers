"use client";

interface StatusIndicatorsProps {
  isInfected: boolean;
  isDizzyFromMagic: boolean;
  isEncumbered: boolean;
}

const STATUSES = [
  { key: "isInfected", glyph: "\u2623", label: "INFECTED", color: "text-[#ff1493]", hint: "" },
  { key: "isDizzyFromMagic", glyph: "\u2728", label: "ARCANE DAZE", color: "text-[#ffe000]", hint: "" },
  {
    key: "isEncumbered",
    glyph: "\u2693",
    label: "ENCUMBERED",
    color: "text-[#8a8a8a]",
    hint: "DR +2 on Strength and Agility \u2014 every swing and every dodge. Drop items to clear it.",
  },
] as const;

export default function StatusIndicators({
  isInfected,
  isDizzyFromMagic,
  isEncumbered,
}: StatusIndicatorsProps) {
  const props: Record<string, boolean> = { isInfected, isDizzyFromMagic, isEncumbered };
  const activeStatuses = STATUSES.filter((s) => props[s.key]);

  if (activeStatuses.length === 0) return null;

  return (
    <>
      <span className="text-xs font-bold uppercase text-[#8a8a8a]">
        STATUS
      </span>
      <div className="space-y-1 mt-2">
        {activeStatuses.map((status) => (
          <div key={status.key}>
            <div className="flex items-center gap-1.5">
              <span className={`${status.color} text-sm`}>{status.glyph}</span>
              <span className={`${status.color} text-xs font-bold uppercase tracking-wider`}>
                {status.label}
              </span>
            </div>
            {status.hint && (
              <p className="text-[#8a8a8a] text-[11px] leading-snug ml-5">{status.hint}</p>
            )}
          </div>
        ))}
      </div>
    </>
  );
}
