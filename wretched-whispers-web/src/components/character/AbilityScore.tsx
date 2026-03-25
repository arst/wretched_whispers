"use client";

interface AbilityScoreProps {
  name: string;
  modifier: number;
}

export default function AbilityScore({ name, modifier }: AbilityScoreProps) {
  const display = modifier > 0 ? `+${modifier}` : `${modifier}`;

  return (
    <div className="flex flex-col items-center p-2 bg-doom-card rounded">
      <span className="text-xs font-bold uppercase text-doom-ash">{name}</span>
      <span className="text-sm text-doom-bone">{display}</span>
    </div>
  );
}
