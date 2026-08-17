"use client";

import type { ReactNode } from "react";
import { useSessionStore, type SessionDrawer } from "@/stores/sessionStore";

interface DrawerToggleProps {
  /** Which drawer this opens; also decides the open/closed half of the aria-label. */
  name: SessionDrawer;
  /** Uppercase chip text. */
  label: string;
  /** Noun in the aria-label when the chip text is not the whole name ("character sheet"). */
  ariaNoun?: string;
  /** Optional bone-coloured detail beside the label — a location, a silver count. */
  badge?: ReactNode;
  /** Extra controls rendered outside the chip but inside the button (HP bar, misery pips). */
  children?: ReactNode;
  /** Hidden (but layout-stable) until the session reaches a state worth opening. */
  visible?: boolean;
  /** Prefixes the chip, e.g. the character sheet's HP readout. */
  leading?: ReactNode;
}

/**
 * The header's drawer buttons. All three fade in on the same rule and share one chip; only the
 * label, the badge and any extra controls differ.
 */
export default function DrawerToggle({
  name,
  label,
  ariaNoun,
  badge,
  children,
  visible = true,
  leading,
}: DrawerToggleProps) {
  const toggleDrawer = useSessionStore((s) => s.toggleDrawer);
  const open = useSessionStore((s) => s.activeDrawer === name);

  return (
    <div
      className={`transition-opacity duration-200 ${
        visible ? "opacity-100" : "pointer-events-none opacity-0"
      }`}
    >
      <button
        onClick={() => toggleDrawer(name)}
        className="flex min-h-[44px] cursor-pointer items-center gap-2"
        aria-label={`${open ? "Close" : "Open"} ${(ariaNoun ?? label).toLowerCase()}`}
      >
        {leading}
        <span className="border-doom-yellow/30 bg-doom-yellow/10 text-doom-yellow hover:border-doom-yellow/60 flex items-center gap-1.5 rounded border px-2 py-1 text-xs font-bold uppercase transition-colors">
          <span>{label}</span>
          {badge && <span className="text-doom-bone">{badge}</span>}
        </span>
        {children}
      </button>
    </div>
  );
}
