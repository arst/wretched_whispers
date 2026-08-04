"use client";

import { useEffect, useRef } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import HpBar from "./HpBar";
import AbilityScore from "./AbilityScore";
import EquipmentSlot from "./EquipmentSlot";
import InventoryList from "./InventoryList";
import InjuryBadges from "./InjuryBadges";
import StatusIndicators from "./StatusIndicators";
import ClassGlyph from "@/components/session/ClassGlyph";

export default function CharacterDrawer() {
  const characterData = useSessionStore((s) => s.characterData);
  const drawerOpen = useSessionStore((s) => s.activeDrawer === "character");
  const toggleDrawer = useSessionStore((s) => s.toggleDrawer);
  const drawerRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    if (drawerOpen && !drawerRef.current?.open) drawerRef.current?.showModal();
    if (!drawerOpen && drawerRef.current?.open) drawerRef.current.close();
  }, [drawerOpen]);

  if (!characterData) return null;

  return (
      <dialog
        ref={drawerRef}
        aria-label="Character sheet"
        onCancel={(event) => { event.preventDefault(); toggleDrawer("character"); }}
        className="fixed inset-y-0 right-0 left-auto m-0 h-full max-h-none w-full sm:w-80 bg-doom-dark text-doom-bone overflow-y-auto backdrop:bg-[#0a0a0a]/60"
      >
        {/* Header row */}
        <div className="px-8 pt-8 pb-0 flex items-start justify-between">
          <div>
            <h2 className="font-display text-lg font-bold text-doom-yellow">
              {characterData.name}
            </h2>
            {characterData.class && (
              <p className="flex items-center gap-1.5 font-body text-xs uppercase tracking-wide text-doom-ash">
                <ClassGlyph characterClass={characterData.class} className="w-4 h-4 shrink-0" />
                {characterData.class}
              </p>
            )}
          </div>
          <button
            onClick={() => toggleDrawer("character")}
            aria-label="Close character sheet"
            className="text-doom-ash hover:text-doom-bone text-xl cursor-pointer"
          >
            {"\u00D7"}
          </button>
        </div>

        {/* HP section */}
        <div className="px-8 pt-6">
          <HpBar
            currentHp={characterData.currentHp}
            maxHp={characterData.maxHp}
            variant="full"
          />
        </div>

        {/* Injuries section */}
        {(characterData.hasLostEye || characterData.hasStabbedLung || characterData.hasBrokenHand || characterData.hasCrushedFoot || characterData.hasSeveredArm || characterData.hasSmashedFace) && (
          <div className="px-8 pt-6">
            <div className="bg-doom-card rounded p-4 border-l-2 border-doom-pink">
              <InjuryBadges characterData={characterData} />
            </div>
          </div>
        )}

        {/* Status section */}
        {(characterData.isInfected || characterData.isDizzyFromMagic || characterData.isEncumbered) && (
          <div className="px-8 pt-6">
            <div className="bg-doom-card rounded p-4 border-l-2 border-doom-yellow">
              <StatusIndicators
                isInfected={characterData.isInfected}
                isDizzyFromMagic={characterData.isDizzyFromMagic}
                isEncumbered={characterData.isEncumbered}
              />
            </div>
          </div>
        )}

        {/* Abilities section */}
        <div className="px-8 pt-6">
          <div className="bg-doom-card rounded p-4 border-l-2 border-doom-yellow">
            <span className="text-xs font-bold uppercase text-doom-ash">
              ABILITIES
            </span>
            <div className="grid grid-cols-2 gap-2 mt-2">
              <AbilityScore name="STR" modifier={characterData.abilities.strength} />
              <AbilityScore name="AGI" modifier={characterData.abilities.agility} />
              <AbilityScore name="PRE" modifier={characterData.abilities.presence} />
              <AbilityScore name="TOU" modifier={characterData.abilities.toughness} />
            </div>
          </div>
        </div>

        {/* Equipment section */}
        <div className="px-8 pt-6">
          <div className="bg-doom-card rounded p-4 border-l-2 border-doom-yellow">
            <span className="text-xs font-bold uppercase text-doom-ash">
              EQUIPMENT
            </span>
            <div className="mt-2 space-y-1">
              <EquipmentSlot label="WEAPON" value={characterData.weapon} />
              <EquipmentSlot label="ARMOR" value={characterData.armor} tier={characterData.armorTier as "none" | "light" | "medium" | "heavy"} />
              {characterData.hasShield && (
                <EquipmentSlot label="SHIELD" value="Shield" isBroken={characterData.isShieldBroken} />
              )}
            </div>
          </div>
        </div>

        {/* Omens section */}
        <div className="px-8 pt-6">
          <div className="bg-doom-card rounded p-4 border-l-2 border-doom-yellow">
            <span className="text-xs font-bold uppercase text-doom-ash">
              OMENS
            </span>
            <div className="mt-2 text-doom-yellow text-sm font-bold">
              {characterData.omens}
            </div>
          </div>
        </div>

        {/* Scrolls section */}
        {characterData.scrolls.length > 0 && (
          <div className="px-8 pt-6">
            <div className="bg-doom-card rounded p-4 border-l-2 border-doom-yellow">
              <span className="text-xs font-bold uppercase text-doom-ash">
                SCROLLS
              </span>
              <div className="mt-2 space-y-1">
                {characterData.scrolls.map((scroll) => (
                  <div key={scroll} className="text-doom-bone text-sm">
                    {scroll}
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* Inventory section */}
        <div className="px-8 pt-6 pb-8">
          <div className="bg-doom-card rounded p-4 border-l-2 border-doom-yellow">
            <span className="text-xs font-bold uppercase text-doom-ash">
              INVENTORY
            </span>
            <div className="mt-2">
              <InventoryList items={characterData.inventory} />
            </div>
          </div>
        </div>
      </dialog>
  );
}
