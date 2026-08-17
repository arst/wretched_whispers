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

export default function EquipmentSlot({
  label,
  value,
  tier,
  isBroken,
}: EquipmentSlotProps) {
  return (
    <div className="flex items-center justify-between py-1">
      <span className="text-doom-ash text-xs font-bold uppercase">{label}</span>
      <span className="flex items-center">
        <span
          className={`text-sm ${
            isBroken
              ? "text-[#8a8a8a] line-through"
              : value
                ? "text-doom-bone"
                : "text-doom-ash"
          }`}
        >
          {value ?? "None"}
        </span>
        {tier && tier !== "none" && TIER_INDICATORS[tier] && (
          <span className="ml-1 text-xs text-[#ffe000]">
            {TIER_INDICATORS[tier]}
          </span>
        )}
      </span>
    </div>
  );
}
