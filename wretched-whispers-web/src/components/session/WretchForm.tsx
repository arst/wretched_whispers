"use client";

import { useState } from "react";
import type { CharacterClass, Difficulty } from "@/types/api";
import Button from "@/components/ui/Button";
import Input from "@/components/ui/Input";

// Player-facing copy. Mirrors the backend CharacterClass enum the same way LEVELS below mirrors
// Difficulty — the blurbs are for the player, and are deliberately not the narrator notes in
// ClassPresets, which are written for the model.
// A null key is "let the dice decide": the request omits the class and the domain rolls it.
const CLASSES: { key: CharacterClass | null; label: string; blurb: string }[] = [
  { key: "FangedDeserter", label: "Fanged Deserter", blurb: "Strong, tusked, and running from a war nobody won. Fights with its teeth." },
  { key: "GutterbornScum", label: "Gutterborn Scum", blurb: "Raised in the runoff. Quick, overlooked, and owed a favour by fortune." },
  { key: "EsotericHermit", label: "Esoteric Hermit", blurb: "Frail, and far too well read. The power comes easier than it should." },
  { key: "OccultHerbmaster", label: "Occult Herbmaster", blurb: "A poisoner with manners. Reads rot and root like scripture." },
  { key: "HereticalPriest", label: "Heretical Priest", blurb: "Ordained, then cast out for preaching the wrong end of the world." },
  { key: "CursedSkinwalker", label: "Cursed Skinwalker", blurb: "Wears a beast's hide that never came off. Something under it is awake." },
  { key: "Classless", label: "Classless Scum", blurb: "Nothing but a name, raw hunger, and terrible luck." },
  { key: null, label: "Let the dice decide", blurb: "Take whatever manner of ruin you were made for." },
];

const LEVELS: { key: Difficulty; label: string; blurb: string }[] = [
  { key: "StoryMode", label: "Story Mode", blurb: "Experience the world. Death is rare; wounds are shallow." },
  { key: "Grim", label: "Grim", blurb: "Measured danger. Bleak, but survivable if you're careful." },
  { key: "Doomed", label: "Doomed", blurb: "True MORK BORG. Unfair, brutal, and often fatal." },
  { key: "Hardcore", label: "Hardcore", blurb: "The world wants you dead. It usually gets its way." },
];

function OptionButton({
  label,
  blurb,
  selected,
  onClick,
}: {
  label: string;
  blurb: string;
  selected: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onClick}
      className={`text-left p-2.5 border transition-colors cursor-pointer ${
        selected
          ? "border-doom-yellow bg-doom-yellow/10"
          : "border-doom-card hover:border-doom-yellow/30"
      }`}
    >
      <div className="font-display text-doom-bone text-sm uppercase tracking-wider">{label}</div>
      <div className="text-doom-ash text-xs mt-1 leading-snug">{blurb}</div>
    </button>
  );
}

// null class means "let the dice decide" — the request omits it and the domain rolls.
export interface WretchChoices {
  characterName: string;
  characterClass: CharacterClass | null;
  difficulty?: Difficulty;
}

interface WretchFormProps {
  title: string;
  intro?: string;
  /** Omitted for successors: difficulty belongs to the campaign, and a death does not renegotiate it. */
  withDifficulty?: boolean;
  confirmLabel: string;
  error?: string;
  busy?: boolean;
  onConfirm: (choices: WretchChoices) => void;
  onCancel: () => void;
}

export default function WretchForm({
  title,
  intro,
  withDifficulty,
  confirmLabel,
  error,
  busy,
  onConfirm,
  onCancel,
}: WretchFormProps) {
  const [name, setName] = useState("");
  const [characterClass, setCharacterClass] = useState<CharacterClass | null>(null);
  const [difficulty, setDifficulty] = useState<Difficulty>("Grim");
  const [nameError, setNameError] = useState("");

  function submit() {
    const trimmed = name.trim();
    if (!trimmed) {
      setNameError("A wretch needs a name.");
      return;
    }
    setNameError("");
    onConfirm({
      characterName: trimmed,
      characterClass,
      difficulty: withDifficulty ? difficulty : undefined,
    });
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-[#0a0a0a]/70 p-4">
      {/* Header and footer are pinned; only the options scroll, so the confirm button is always reachable. */}
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="w-full max-w-5xl max-h-full flex flex-col bg-doom-dark border border-doom-card"
      >
        <div className="px-6 pt-6 pb-4 border-b border-doom-card">
          <h2 className="font-display text-doom-yellow text-xl tracking-wider mb-1">{title}</h2>
          {intro && <p className="text-doom-ash text-sm">{intro}</p>}
        </div>

        <div className="px-6 py-5 overflow-y-auto flex flex-col gap-5">
          <Input
            label="What name is carved into your wretched hide?"
            placeholder="Speak, wretch..."
            value={name}
            maxLength={64}
            autoFocus
            error={nameError}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") submit();
            }}
          />

          <fieldset>
            <legend className="text-doom-ash text-xs uppercase tracking-wider mb-2">
              And what are you?
            </legend>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2">
              {CLASSES.map((cls) => (
                <OptionButton
                  key={String(cls.key)}
                  label={cls.label}
                  blurb={cls.blurb}
                  selected={characterClass === cls.key}
                  onClick={() => setCharacterClass(cls.key)}
                />
              ))}
            </div>
          </fieldset>

          {withDifficulty && (
            <fieldset>
              <legend className="text-doom-ash text-xs uppercase tracking-wider mb-2">
                Choose your doom
              </legend>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2">
                {LEVELS.map((lvl) => (
                  <OptionButton
                    key={lvl.key}
                    label={lvl.label}
                    blurb={lvl.blurb}
                    selected={difficulty === lvl.key}
                    onClick={() => setDifficulty(lvl.key)}
                  />
                ))}
              </div>
            </fieldset>
          )}
        </div>

        <div className="px-6 pb-6 pt-4 border-t border-doom-card flex items-center justify-end gap-3">
          {error && <p className="text-doom-pink text-sm mr-auto">{error}</p>}
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button variant="primary" onClick={submit} loading={busy}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
