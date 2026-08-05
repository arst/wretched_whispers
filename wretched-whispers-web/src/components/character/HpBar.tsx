"use client";

interface HpBarProps {
  currentHp: number;
  maxHp: number;
  variant?: "mini" | "full";
}

/** Ash when down, yellow above half, pink to a quarter, blood below. Shared with the header pill. */
export function hpTone(currentHp: number, maxHp: number): string {
  if (currentHp <= 0) return "#8a8a8a";
  const pct = (currentHp / maxHp) * 100;
  if (pct > 50) return "#ffe000";
  if (pct >= 26) return "#ff1493";
  return "#8b0000";
}

export default function HpBar({ currentHp, maxHp, variant = "full" }: HpBarProps) {
  const isMini = variant === "mini";
  const heightClass = isMini ? "h-2" : "h-4";
  const fillPct = maxHp > 0 ? Math.max(0, Math.min(100, (currentHp / maxHp) * 100)) : 0;

  return (
    <div
      className={`relative w-full ${heightClass} bg-doom-card rounded overflow-hidden`}
      role="progressbar"
      aria-valuenow={currentHp}
      aria-valuemin={0}
      aria-valuemax={maxHp}
      aria-label="Hit points"
    >
      <div
        className="absolute inset-y-0 left-0 rounded transition-all duration-300 ease-in-out"
        style={{ width: `${fillPct}%`, backgroundColor: hpTone(currentHp, maxHp) }}
      />
      {!isMini && (
        <span className="absolute inset-0 flex items-center justify-center text-xs font-bold text-doom-bone z-10">
          HP {currentHp}/{maxHp}
        </span>
      )}
    </div>
  );
}
