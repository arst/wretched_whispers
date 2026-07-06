"use client";

import { useState } from "react";
import type { Difficulty } from "@/types/api";
import Button from "@/components/ui/Button";

const LEVELS: { key: Difficulty; label: string; blurb: string }[] = [
  { key: "StoryMode", label: "Story Mode", blurb: "Experience the world. Death is rare; wounds are shallow." },
  { key: "Grim", label: "Grim", blurb: "Measured danger. Bleak, but survivable if you're careful." },
  { key: "Doomed", label: "Doomed", blurb: "True MORK BORG. Unfair, brutal, and often fatal." },
  { key: "Hardcore", label: "Hardcore", blurb: "The world wants you dead. It usually gets its way." },
];

interface DifficultyPickerProps {
  onConfirm: (difficulty: Difficulty) => void;
  onCancel: () => void;
  busy?: boolean;
}

export default function DifficultyPicker({ onConfirm, onCancel, busy }: DifficultyPickerProps) {
  const [selected, setSelected] = useState<Difficulty>("Grim");

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-[#0a0a0a]/70 px-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Choose difficulty"
        className="w-full max-w-md bg-doom-dark border border-doom-card p-6"
      >
        <h2 className="font-display text-doom-yellow text-xl tracking-wider mb-4">
          CHOOSE YOUR DOOM
        </h2>
        <div className="flex flex-col gap-2 mb-6">
          {LEVELS.map((lvl) => (
            <button
              key={lvl.key}
              onClick={() => setSelected(lvl.key)}
              className={`text-left p-3 border transition-colors ${
                selected === lvl.key
                  ? "border-doom-yellow bg-doom-yellow/10"
                  : "border-doom-card hover:border-doom-yellow/30"
              }`}
            >
              <div className="font-display text-doom-bone text-sm uppercase tracking-wider">
                {lvl.label}
              </div>
              <div className="text-doom-ash text-xs mt-1">{lvl.blurb}</div>
            </button>
          ))}
        </div>
        <div className="flex justify-end gap-3">
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button variant="primary" onClick={() => onConfirm(selected)} loading={busy}>
            Begin
          </Button>
        </div>
      </div>
    </div>
  );
}
