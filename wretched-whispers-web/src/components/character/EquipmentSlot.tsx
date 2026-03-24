"use client";

interface EquipmentSlotProps {
  label: string;
  value: string | null;
}

export default function EquipmentSlot({ label, value }: EquipmentSlotProps) {
  return (
    <div className="flex items-center justify-between py-1">
      <span className="text-xs font-bold uppercase text-doom-ash">{label}</span>
      <span className={`text-sm ${value ? "text-doom-bone" : "text-doom-ash"}`}>
        {value ?? "None"}
      </span>
    </div>
  );
}
