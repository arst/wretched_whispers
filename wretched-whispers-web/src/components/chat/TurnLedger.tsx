"use client";

import type { TurnDeltaEvent } from "@/types/api";

interface TurnLedgerProps {
  delta: TurnDeltaEvent;
}

type Tone = "gain" | "loss" | "harm";

interface Entry {
  text: string;
  tone: Tone;
}

const TONE_CLASS: Record<Tone, string> = {
  gain: "text-emerald-400/90",
  loss: "text-doom-bone",
  harm: "text-red-400/90",
};

function signed(n: number): string {
  return n > 0 ? `+${n}` : `${n}`;
}

// Turn the authoritative diff into human-readable chips. Order: resources, gear, body, time, doom.
function toEntries(d: TurnDeltaEvent): Entry[] {
  const entries: Entry[] = [];

  if (d.silverChange !== 0)
    entries.push({
      text: `${signed(d.silverChange)} silver`,
      tone: d.silverChange > 0 ? "gain" : "loss",
    });
  if (d.hpChange !== 0)
    entries.push({
      text: `${signed(d.hpChange)} HP`,
      tone: d.hpChange > 0 ? "gain" : "harm",
    });

  for (const item of d.itemsAdded)
    entries.push({ text: `+ ${item}`, tone: "gain" });
  for (const item of d.itemsRemoved)
    entries.push({ text: `− ${item}`, tone: "loss" });

  const abilities: [number, string][] = [
    [d.strengthChange, "Strength"],
    [d.agilityChange, "Agility"],
    [d.presenceChange, "Presence"],
    [d.toughnessChange, "Toughness"],
  ];
  for (const [change, name] of abilities)
    if (change !== 0)
      entries.push({
        text: `${signed(change)} ${name}`,
        tone: change > 0 ? "gain" : "harm",
      });

  for (const affliction of d.newAfflictions)
    entries.push({ text: affliction, tone: "harm" });

  if (d.hoursElapsed > 0)
    entries.push({ text: `${d.hoursElapsed}h pass`, tone: "loss" });
  if (d.miseryChange > 0)
    entries.push({ text: `${signed(d.miseryChange)} Misery`, tone: "harm" });
  if (d.died) entries.push({ text: "Slain", tone: "harm" });
  if (d.worldEnded) entries.push({ text: "The world ends", tone: "harm" });

  return entries;
}

export default function TurnLedger({ delta }: TurnLedgerProps) {
  const entries = toEntries(delta);

  return (
    <div className="border-doom-yellow/40 bg-doom-dark rounded border px-3 py-2">
      <p className="text-doom-yellow mb-1 text-xs font-bold tracking-wider uppercase">
        This Turn
      </p>
      {entries.length === 0 ? (
        // The authoritative "nothing happened" — this is what contradicts a narration that
        // claimed an outcome no tool applied (e.g. a purchase never made).
        <p className="text-sm text-[#8a8a8a] italic">Nothing changed.</p>
      ) : (
        <div className="flex flex-wrap gap-x-3 gap-y-1">
          {entries.map((entry, i) => (
            <span
              key={i}
              className={`font-mono text-sm ${TONE_CLASS[entry.tone]}`}
            >
              {entry.text}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
