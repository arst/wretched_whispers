"use client";

import { useSessionStore } from "@/stores/sessionStore";
import HpBar from "./HpBar";
import MiseryTracker from "./MiseryTracker";

function getHpTextColor(currentHp: number, maxHp: number): string {
  if (currentHp <= 0) return "text-[#8a8a8a]";
  const pct = (currentHp / maxHp) * 100;
  if (pct > 50) return "text-[#ffe000]";
  if (pct >= 26) return "text-[#ff1493]";
  return "text-[#8b0000]";
}

export default function CharacterDrawerToggle() {
  const characterData = useSessionStore((s) => s.characterData);
  const toggleDrawer = useSessionStore((s) => s.toggleDrawer);
  const status = useSessionStore((s) => s.status);
  const drawerOpen = useSessionStore((s) => s.drawerOpen);

  const miseryCount = useSessionStore((s) => s.miseryCount);

  const isVisible = (status === "in-progress" || status === "ended") && characterData !== null;
  const silver = characterData?.silver;

  return (
    <div
      className={`transition-opacity duration-200 ${
        isVisible ? "opacity-100" : "opacity-0 pointer-events-none"
      }`}
    >
      <button
        onClick={toggleDrawer}
        className="min-h-[44px] flex items-center gap-2 cursor-pointer"
        aria-label={drawerOpen ? "Close character sheet" : "Open character sheet"}
      >
        <span className="flex items-center gap-1.5 rounded border border-doom-card bg-doom-card/60 px-2 py-1">
          <span className={`text-xs font-bold ${characterData ? getHpTextColor(characterData.currentHp, characterData.maxHp) : "text-doom-ash"}`}>
            HP
          </span>
          <span className={`text-xs font-bold ${characterData ? getHpTextColor(characterData.currentHp, characterData.maxHp) : "text-doom-ash"}`}>
            {characterData ? `${characterData.currentHp}/${characterData.maxHp}` : ""}
          </span>
        </span>
        <span className="flex items-center gap-1.5 rounded border border-doom-yellow/30 bg-doom-yellow/10 px-2 py-1 text-xs font-bold uppercase text-doom-yellow hover:border-doom-yellow/60 transition-colors">
          <span>Character</span>
          {silver !== null && silver !== undefined && (
            <span className="text-doom-bone">{silver} silver</span>
          )}
        </span>
        {characterData && (
          <div className="hidden sm:block w-16">
            <HpBar
              currentHp={characterData.currentHp}
              maxHp={characterData.maxHp}
              variant="mini"
            />
          </div>
        )}
        <MiseryTracker count={miseryCount} />
      </button>
    </div>
  );
}
