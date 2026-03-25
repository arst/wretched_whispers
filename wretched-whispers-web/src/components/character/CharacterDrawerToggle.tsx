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
        <span className={`text-xs font-bold ${characterData ? getHpTextColor(characterData.currentHp, characterData.maxHp) : "text-doom-ash"}`}>
          HP
        </span>
        <span className={`text-xs font-bold ${characterData ? getHpTextColor(characterData.currentHp, characterData.maxHp) : "text-doom-ash"}`}>
          {characterData ? `${characterData.currentHp}/${characterData.maxHp}` : ""}
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
