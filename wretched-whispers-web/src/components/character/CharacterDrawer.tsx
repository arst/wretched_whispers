"use client";

import { useSessionStore } from "@/stores/sessionStore";
import Drawer from "@/components/ui/Drawer";
import HpBar from "./HpBar";
import EquipmentSlot from "./EquipmentSlot";
import InventoryList from "./InventoryList";
import InjuryBadges from "./InjuryBadges";
import StatusIndicators from "./StatusIndicators";
import ClassGlyph from "@/components/session/ClassGlyph";

function Section({
  title,
  accent = "border-doom-yellow",
  children,
}: {
  title?: string;
  accent?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="px-8 pt-6 last:pb-8">
      <div className={`bg-doom-card rounded p-4 border-l-2 ${accent}`}>
        {title && (
          <span className="text-xs font-bold uppercase text-doom-ash">{title}</span>
        )}
        <div className="mt-2">{children}</div>
      </div>
    </div>
  );
}

export default function CharacterDrawer() {
  const characterData = useSessionStore((s) => s.characterData);

  if (!characterData) return null;

  const characterClass = characterData.characterClass;
  const abilities = [
    ["STR", characterData.characterStrength ?? 0],
    ["AGI", characterData.characterAgility ?? 0],
    ["PRE", characterData.characterPresence ?? 0],
    ["TOU", characterData.characterToughness ?? 0],
  ] as const;
  const injured =
    characterData.hasLostEye ||
    characterData.hasStabbedLung ||
    characterData.hasBrokenHand ||
    characterData.hasCrushedFoot ||
    characterData.hasSeveredArm ||
    characterData.hasSmashedFace;
  const afflicted =
    characterData.isInfected ||
    characterData.isDizzyFromMagic ||
    characterData.isEncumbered;
  const scrolls = characterData.characterScrolls ?? [];

  return (
    <Drawer
      name="character"
      label="Character sheet"
      title={characterData.characterName ?? ""}
      subtitle={
        characterClass && (
          <p className="flex items-center gap-1.5 font-body text-xs uppercase tracking-wide text-doom-ash">
            <ClassGlyph characterClass={characterClass} className="w-4 h-4 shrink-0" />
            {characterClass}
          </p>
        )
      }
    >
      <div className="px-8 pt-6">
        <HpBar
          currentHp={characterData.characterHp ?? 0}
          maxHp={characterData.characterMaxHp ?? 0}
          variant="full"
        />
      </div>

      {injured && (
        <Section title="INJURIES" accent="border-doom-pink">
          <InjuryBadges characterData={characterData} />
        </Section>
      )}

      {afflicted && (
        <Section title="STATUS">
          <StatusIndicators characterData={characterData} />
        </Section>
      )}

      <Section title="ABILITIES">
        <div className="grid grid-cols-2 gap-2">
          {abilities.map(([name, score]) => (
            <div key={name} className="flex flex-col items-center p-2 bg-doom-card rounded">
              <span className="text-xs font-bold uppercase text-doom-ash">{name}</span>
              <span className="text-sm text-doom-bone">
                {score > 0 ? `+${score}` : score}
              </span>
            </div>
          ))}
        </div>
      </Section>

      <Section title="EQUIPMENT">
        <div className="space-y-1">
          <EquipmentSlot label="WEAPON" value={characterData.characterWeapon ?? null} />
          <EquipmentSlot
            label="ARMOR"
            value={characterData.characterArmor ?? null}
            tier={characterData.armorTier as "none" | "light" | "medium" | "heavy"}
          />
          {characterData.hasShield && (
            <EquipmentSlot
              label="SHIELD"
              value="Shield"
              isBroken={characterData.isShieldBroken}
            />
          )}
        </div>
      </Section>

      <Section title="OMENS">
        <div className="text-doom-yellow text-sm font-bold">
          {characterData.characterOmens ?? 0}
        </div>
      </Section>

      {scrolls.length > 0 && (
        <Section title="SCROLLS">
          <div className="space-y-1">
            {scrolls.map((scroll) => (
              <div key={scroll} className="text-doom-bone text-sm">
                {scroll}
              </div>
            ))}
          </div>
        </Section>
      )}

      <Section title="INVENTORY">
        <InventoryList items={characterData.characterInventory ?? []} />
      </Section>
    </Drawer>
  );
}
