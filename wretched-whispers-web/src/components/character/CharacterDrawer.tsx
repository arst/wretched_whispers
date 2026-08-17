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
      <div className={`bg-doom-card rounded border-l-2 p-4 ${accent}`}>
        {title && (
          <span className="text-doom-ash text-xs font-bold uppercase">
            {title}
          </span>
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
          <p className="font-body text-doom-ash flex items-center gap-1.5 text-xs tracking-wide uppercase">
            <ClassGlyph
              characterClass={characterClass}
              className="h-4 w-4 shrink-0"
            />
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
            <div
              key={name}
              className="bg-doom-card flex flex-col items-center rounded p-2"
            >
              <span className="text-doom-ash text-xs font-bold uppercase">
                {name}
              </span>
              <span className="text-doom-bone text-sm">
                {score > 0 ? `+${score}` : score}
              </span>
            </div>
          ))}
        </div>
      </Section>

      <Section title="EQUIPMENT">
        <div className="space-y-1">
          <EquipmentSlot
            label="WEAPON"
            value={characterData.characterWeapon ?? null}
          />
          <EquipmentSlot
            label="ARMOR"
            value={characterData.characterArmor ?? null}
            tier={
              characterData.armorTier as "none" | "light" | "medium" | "heavy"
            }
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
