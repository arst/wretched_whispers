"use client";

import { useEffect, useState } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import { apiFetch } from "@/lib/api";
import Drawer from "@/components/ui/Drawer";
import type { JournalEntryDto, FallenCharacterDto } from "@/types/api";

export default function JournalDrawer() {
  const sessionId = useSessionStore((s) => s.sessionId);
  const journalOpen = useSessionStore((s) => s.activeDrawer === "journal");
  const miseryPsalms = useSessionStore((s) => s.miseryPsalms);
  const [entries, setEntries] = useState<JournalEntryDto[] | null>(null);
  const [fallen, setFallen] = useState<FallenCharacterDto[]>([]);

  // Fetch on open so entries recorded during play show up without SSE plumbing
  useEffect(() => {
    if (!journalOpen || !sessionId) return;
    const controller = new AbortController();
    apiFetch(`/sessions/${sessionId}/journal`, { signal: controller.signal })
      .then((r) => (r.ok ? r.json() : Promise.reject()))
      .then((data) => {
        if (controller.signal.aborted) return;
        setEntries(data.entries);
        setFallen(data.fallen ?? []);
      })
      .catch(() => {
        if (controller.signal.aborted) return;
        setEntries([]);
        setFallen([]);
      });
    return () => controller.abort();
  }, [journalOpen, sessionId]);

  return (
      <Drawer name="journal" title="JOURNAL" label="Campaign journal">
        <div className="px-8 pt-6 pb-8 space-y-4">
          {miseryPsalms.length > 0 && (
            <div className="bg-doom-card rounded p-4 border-l-2 border-doom-pink">
              <span className="text-xs font-bold uppercase text-doom-ash">
                MISERIES {miseryPsalms.length}/7
              </span>
              <div className="mt-2 space-y-1">
                {miseryPsalms.map((psalm) => (
                  <div key={psalm} className="text-doom-pink text-sm">
                    {psalm}
                  </div>
                ))}
              </div>
            </div>
          )}
          {fallen.length > 0 && (
            <div className="bg-doom-card rounded p-4 border-l-2 border-doom-ash">
              <span className="text-xs font-bold uppercase text-doom-ash">
                GRAVEYARD
              </span>
              <div className="mt-2 space-y-1">
                {fallen.map((f, i) => (
                  <div key={i} className="text-doom-bone text-sm">
                    &#9760; {f.name} — died day {f.dayDied}
                  </div>
                ))}
              </div>
            </div>
          )}
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
      </Drawer>
  );
}
