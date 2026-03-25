"use client";

interface EquipmentSlotProps {
  label: string;
  value: string | null;
  tier?: "none" | "light" | "medium" | "heavy";
  isBroken?: boolean;
}

const TIER_INDICATORS: Record<string, string> = {
  light: "\u25A0",
  medium: "\u25A0\u25A0",
  heavy: "\u25A0\u25A0\u25A0",
};

export default function EquipmentSlot({ label, value, tier, isBroken }: EquipmentSlotProps) {
  return (
    <div className="flex items-center justify-between py-1">
      <span className="text-xs font-bold uppercase text-doom-ash">{label}</span>
      <span className="flex items-center">
        <span
          className={`text-sm ${
            isBroken
              ? "line-through text-[#8a8a8a]"
              : value
                ? "text-doom-bone"
                : "text-doom-ash"
          }`}
        >
          {value ?? "None"}
        </span>
        {tier && tier !== "none" && TIER_INDICATORS[tier] && (
          <span className="text-[#ffe000] text-xs ml-1">
            {TIER_INDICATORS[tier]}
          </span>
        )}
      </span>
    </div>
  );
}
