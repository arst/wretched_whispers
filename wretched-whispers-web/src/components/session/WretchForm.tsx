"use client";

import { useState } from "react";
import type { CharacterClass, Difficulty } from "@/types/api";
import Button from "@/components/ui/Button";
import Input from "@/components/ui/Input";

// Player-facing copy. Mirrors the backend CharacterClass enum the same way LEVELS below mirrors
// Difficulty — the blurbs are for the player, and are deliberately not the narrator notes in
// ClassPresets, which are written for the model.
const CLASSES: { key: CharacterClass; label: string; blurb: string }[] = [
  { key: "FangedDeserter", label: "Fanged Deserter", blurb: "Strong, tusked, and running from a war nobody won. Fights with its teeth." },
  { key: "GutterbornScum", label: "Gutterborn Scum", blurb: "Raised in the runoff. Quick, overlooked, and owed a favour by fortune." },
  { key: "EsotericHermit", label: "Esoteric Hermit", blurb: "Frail, and far too well read. The power comes easier than it should." },
  { key: "OccultHerbmaster", label: "Occult Herbmaster", blurb: "A poisoner with manners. Reads rot and root like scripture." },
  { key: "HereticalPriest", label: "Heretical Priest", blurb: "Ordained, then cast out for preaching the wrong end of the world." },
  { key: "CursedSkinwalker", label: "Cursed Skinwalker", blurb: "Wears a beast's hide that never came off. Something under it is awake." },
  { key: "Classless", label: "Classless Scum", blurb: "Nothing but a name, raw hunger, and terrible luck." },
];

const LEVELS: { key: Difficulty; label: string; blurb: string }[] = [
  { key: "StoryMode", label: "Story Mode", blurb: "Experience the world. Death is rare; wounds are shallow." },
  { key: "Grim", label: "Grim", blurb: "Measured danger. Bleak, but survivable if you're careful." },
  { key: "Doomed", label: "Doomed", blurb: "True MORK BORG. Unfair, brutal, and often fatal." },
  { key: "Hardcore", label: "Hardcore", blurb: "The world wants you dead. It usually gets its way." },
];

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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-[#0a0a0a]/70 px-4 py-8 overflow-y-auto">
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="w-full max-w-md bg-doom-dark border border-doom-card p-6 my-auto"
      >
        <h2 className="font-display text-doom-yellow text-xl tracking-wider mb-1">{title}</h2>
        {intro && <p className="text-doom-ash text-sm mb-5">{intro}</p>}

        <div className="mb-6">
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
        </div>

        <fieldset className="mb-6">
          <legend className="text-doom-ash text-xs uppercase tracking-wider mb-1.5">
            And what are you?
          </legend>
          <div className="flex flex-col gap-2">
            {CLASSES.map((cls) => (
              <button
                key={cls.key}
                type="button"
                aria-pressed={characterClass === cls.key}
                onClick={() => setCharacterClass(cls.key)}
                className={`text-left p-3 border transition-colors ${
                  characterClass === cls.key
                    ? "border-doom-yellow bg-doom-yellow/10"
                    : "border-doom-card hover:border-doom-yellow/30"
                }`}
              >
                <div className="font-display text-doom-bone text-sm uppercase tracking-wider">
                  {cls.label}
                </div>
                <div className="text-doom-ash text-xs mt-1">{cls.blurb}</div>
              </button>
            ))}
            <button
              type="button"
              aria-pressed={characterClass === null}
              onClick={() => setCharacterClass(null)}
              className={`text-left p-3 border transition-colors ${
                characterClass === null
                  ? "border-doom-yellow bg-doom-yellow/10"
                  : "border-doom-card hover:border-doom-yellow/30"
              }`}
            >
              <div className="font-display text-doom-bone text-sm uppercase tracking-wider">
                Let the dice decide
              </div>
              <div className="text-doom-ash text-xs mt-1">
                Take whatever manner of ruin you were made for.
              </div>
            </button>
          </div>
        </fieldset>

        {withDifficulty && (
          <fieldset className="mb-6">
            <legend className="text-doom-ash text-xs uppercase tracking-wider mb-1.5">
              Choose your doom
            </legend>
            <div className="flex flex-col gap-2">
              {LEVELS.map((lvl) => (
                <button
                  key={lvl.key}
                  type="button"
                  aria-pressed={difficulty === lvl.key}
                  onClick={() => setDifficulty(lvl.key)}
                  className={`text-left p-3 border transition-colors ${
                    difficulty === lvl.key
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
          </fieldset>
        )}

        {error && <p className="text-doom-pink text-sm mb-3">{error}</p>}

        <div className="flex justify-end gap-3">
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
