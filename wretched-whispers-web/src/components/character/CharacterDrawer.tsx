"use client";

import { useEffect, useRef, useState } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import HpBar from "./HpBar";
import AbilityScore from "./AbilityScore";
import EquipmentSlot from "./EquipmentSlot";
import InventoryList from "./InventoryList";
import InjuryBadges from "./InjuryBadges";
import StatusIndicators from "./StatusIndicators";

export default function CharacterDrawer() {
  const characterData = useSessionStore((s) => s.characterData);
  const drawerOpen = useSessionStore((s) => s.drawerOpen);
  const toggleDrawer = useSessionStore((s) => s.toggleDrawer);
  const drawerRef = useRef<HTMLDivElement>(null);
  const [mounted, setMounted] = useState(false);

  // Mount/unmount with transition support
  useEffect(() => {
    if (drawerOpen) {
      setMounted(true);
    } else {
      const timer = setTimeout(() => setMounted(false), 200);
      return () => clearTimeout(timer);
    }
  }, [drawerOpen]);

  // Focus trap
  useEffect(() => {
    if (!drawerOpen || !drawerRef.current) return;

    const previousFocus = document.activeElement as HTMLElement | null;
    const drawer = drawerRef.current;

    const focusableSelector =
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';

    function getFocusableElements() {
      return Array.from(
        drawer.querySelectorAll<HTMLElement>(focusableSelector)
      );
    }

    // Focus first element after transition
    const focusTimer = setTimeout(() => {
      const elements = getFocusableElements();
      if (elements.length > 0) elements[0].focus();
    }, 50);

    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        toggleDrawer();
        return;
      }

      if (e.key === "Tab") {
        const elements = getFocusableElements();
        if (elements.length === 0) return;

        const first = elements[0];
        const last = elements[elements.length - 1];

        if (e.shiftKey) {
          if (document.activeElement === first) {
            e.preventDefault();
            last.focus();
          }
        } else {
          if (document.activeElement === last) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    }

    document.addEventListener("keydown", handleKeyDown);

    return () => {
      clearTimeout(focusTimer);
      document.removeEventListener("keydown", handleKeyDown);
      if (previousFocus && typeof previousFocus.focus === "function") {
        previousFocus.focus();
      }
    };
  }, [drawerOpen, toggleDrawer]);

  if (!characterData || !mounted) return null;

  return (
    <>
      {/* Backdrop */}
      <div
        className={`fixed inset-0 z-50 bg-[#0a0a0a]/60 transition-opacity duration-200 ${
          drawerOpen ? "opacity-100" : "opacity-0"
        }`}
        onClick={toggleDrawer}
        aria-hidden="true"
      />

      {/* Drawer panel */}
      <div
        ref={drawerRef}
        role="dialog"
        aria-modal="true"
        aria-label="Character sheet"
        className={`fixed top-0 right-0 z-50 h-full w-full sm:w-80 bg-doom-dark transform transition-transform duration-200 ease-out overflow-y-auto ${
          drawerOpen ? "translate-x-0" : "translate-x-full"
        }`}
      >
        {/* Header row */}
        <div className="px-8 pt-8 pb-0 flex items-center justify-between">
          <h2 className="font-display text-lg font-bold text-doom-yellow">
            {characterData.name}
          </h2>
          <button
            onClick={toggleDrawer}
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
      </div>
    </>
  );
}
