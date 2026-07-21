"use client";

import { useEffect, useRef, useState } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import { apiFetch } from "@/lib/api";
import type { JournalEntryDto } from "@/types/api";

export default function JournalDrawer() {
  const sessionId = useSessionStore((s) => s.sessionId);
  const journalOpen = useSessionStore((s) => s.journalOpen);
  const toggleJournal = useSessionStore((s) => s.toggleJournal);
  const drawerRef = useRef<HTMLDivElement>(null);
  const [mounted, setMounted] = useState(false);
  const [entries, setEntries] = useState<JournalEntryDto[] | null>(null);

  // Mount/unmount with transition support
  useEffect(() => {
    if (journalOpen) {
      setMounted(true);
    } else {
      const timer = setTimeout(() => setMounted(false), 200);
      return () => clearTimeout(timer);
    }
  }, [journalOpen]);

  // Fetch on open so entries recorded during play show up without SSE plumbing
  useEffect(() => {
    if (!journalOpen || !sessionId) return;
    setEntries(null);
    apiFetch(`/sessions/${sessionId}/journal`)
      .then((r) => (r.ok ? r.json() : Promise.reject()))
      .then((data) => setEntries(data.entries))
      .catch(() => setEntries([]));
  }, [journalOpen, sessionId]);

  // Focus trap
  useEffect(() => {
    if (!journalOpen || !drawerRef.current) return;

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
        toggleJournal();
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
  }, [journalOpen, toggleJournal]);

  if (!mounted) return null;

  return (
    <>
      {/* Backdrop */}
      <div
        className={`fixed inset-0 z-50 bg-[#0a0a0a]/60 transition-opacity duration-200 ${
          journalOpen ? "opacity-100" : "opacity-0"
        }`}
        onClick={toggleJournal}
        aria-hidden="true"
      />

      {/* Drawer panel */}
      <div
        ref={drawerRef}
        role="dialog"
        aria-modal="true"
        aria-label="Campaign journal"
        className={`fixed top-0 right-0 z-50 h-full w-full sm:w-80 bg-doom-dark transform transition-transform duration-200 ease-out overflow-y-auto ${
          journalOpen ? "translate-x-0" : "translate-x-full"
        }`}
      >
        {/* Header row */}
        <div className="px-8 pt-8 pb-0 flex items-center justify-between">
          <h2 className="font-display text-lg font-bold text-doom-yellow">
            JOURNAL
          </h2>
          <button
            onClick={toggleJournal}
            aria-label="Close journal"
            className="text-doom-ash hover:text-doom-bone text-xl cursor-pointer"
          >
            {"×"}
          </button>
        </div>

        <div className="px-8 pt-6 pb-8 space-y-4">
          {entries === null && (
            <p className="text-doom-ash text-sm">Loading...</p>
          )}
          {entries !== null && entries.length === 0 && (
            <p className="text-doom-ash text-sm">Nothing recorded yet.</p>
          )}
          {entries?.map((entry, i) => (
            <div
              key={i}
              className="bg-doom-card rounded p-4 border-l-2 border-doom-yellow"
            >
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold uppercase text-doom-ash">
                  DAY {entry.day} {"·"} {entry.hour}:00
                </span>
                <span className="text-doom-yellow text-xs font-bold uppercase tracking-wider">
                  {entry.category}
                </span>
              </div>
              <p className="mt-2 text-doom-bone text-sm">{entry.text}</p>
            </div>
          ))}
        </div>
      </div>
    </>
  );
}
