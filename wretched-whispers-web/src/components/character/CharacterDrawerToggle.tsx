"use client";

import { useSessionStore } from "@/stores/sessionStore";
import DrawerToggle from "@/components/ui/DrawerToggle";
import HpBar, { hpTone } from "./HpBar";
import MiseryTracker from "./MiseryTracker";

export default function CharacterDrawerToggle() {
  const characterData = useSessionStore((s) => s.characterData);
  const status = useSessionStore((s) => s.status);
  const miseryCount = useSessionStore((s) => s.miseryCount);

  const silver = characterData?.characterSilver;
  const hp = characterData?.characterHp ?? 0;
  const maxHp = characterData?.characterMaxHp ?? 0;

  return (
    <DrawerToggle
      name="character"
      label="Character"
      ariaNoun="character sheet"
      visible={
        (status === "in-progress" || status === "ended") &&
        characterData !== null
      }
      badge={
        silver !== null && silver !== undefined ? `${silver} silver` : undefined
      }
      leading={
        <span
          className={`border-doom-card bg-doom-card/60 flex items-center gap-1.5 rounded border px-2 py-1 text-xs font-bold ${
            characterData ? "" : "text-doom-ash"
          }`}
          style={characterData ? { color: hpTone(hp, maxHp) } : undefined}
        >
          <span>HP</span>
          <span>{characterData ? `${hp}/${maxHp}` : ""}</span>
        </span>
      }
    >
      {characterData && (
        <div className="hidden w-16 sm:block">
          <HpBar currentHp={hp} maxHp={maxHp} variant="mini" />
        </div>
      )}
      <MiseryTracker count={miseryCount} />
    </DrawerToggle>
  );
}
