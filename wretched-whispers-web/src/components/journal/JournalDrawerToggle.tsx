"use client";

import { useSessionStore } from "@/stores/sessionStore";

export default function JournalDrawerToggle() {
  const toggleJournal = useSessionStore((s) => s.toggleJournal);
  const status = useSessionStore((s) => s.status);
  const journalOpen = useSessionStore((s) => s.journalOpen);

  const isVisible = status === "in-progress" || status === "ended";

  return (
    <div
      className={`transition-opacity duration-200 ${
        isVisible ? "opacity-100" : "opacity-0 pointer-events-none"
      }`}
    >
      <button
        onClick={toggleJournal}
        className="min-h-[44px] flex items-center cursor-pointer"
        aria-label={journalOpen ? "Close journal" : "Open journal"}
      >
        <span className="flex items-center rounded border border-doom-yellow/30 bg-doom-yellow/10 px-2 py-1 text-xs font-bold uppercase text-doom-yellow hover:border-doom-yellow/60 transition-colors">
          Journal
        </span>
      </button>
    </div>
  );
}
