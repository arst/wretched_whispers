"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { useSessionStore, type SessionDrawer } from "@/stores/sessionStore";

interface DrawerProps {
  /** Which drawer this is; only one is open at a time (see toggleDrawer). */
  name: SessionDrawer;
  title: string;
  /** Accessible name — the title alone is not enough when it is a character's name. */
  label: string;
  subtitle?: ReactNode;
  /** Tailwind width at sm and up. Written out at the call site so the scanner sees it. */
  width?: string;
  children: ReactNode;
}

/** Right-side sheet on a native <dialog>: modality, focus trap and Esc come for free. */
export default function Drawer({
  name,
  title,
  label,
  subtitle,
  width = "sm:w-80",
  children,
}: DrawerProps) {
  const open = useSessionStore((s) => s.activeDrawer === name);
  const toggleDrawer = useSessionStore((s) => s.toggleDrawer);
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    if (open && !ref.current?.open) ref.current?.showModal();
    if (!open && ref.current?.open) ref.current.close();
  }, [open]);

  return (
    <dialog
      ref={ref}
      aria-label={label}
      onCancel={(event) => {
        event.preventDefault();
        toggleDrawer(name);
      }}
      className={`fixed inset-y-0 right-0 left-auto m-0 h-full max-h-none w-full ${width} bg-doom-dark text-doom-bone overflow-y-auto backdrop:bg-[#0a0a0a]/60`}
    >
      <div className="px-8 pt-8 pb-0 flex items-start justify-between">
        <div>
          <h2 className="font-display text-lg font-bold text-doom-yellow">{title}</h2>
          {subtitle}
        </div>
        <button
          onClick={() => toggleDrawer(name)}
          aria-label={`Close ${label.toLowerCase()}`}
          className="text-doom-ash hover:text-doom-bone text-xl cursor-pointer"
        >
          {"×"}
        </button>
      </div>
      {children}
    </dialog>
  );
}
