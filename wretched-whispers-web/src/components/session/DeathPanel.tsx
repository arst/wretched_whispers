"use client";

import { useState } from "react";
import { apiFetch } from "@/lib/api";

interface DeathPanelProps {
  sessionId: string;
  characterName: string | null;
}

/** Shown when the session status is "fallen": the wretch is dead, the world is not.
 *  Both actions reload the page — the session loader derives the new state from scratch. */
export default function DeathPanel({ sessionId, characterName }: DeathPanelProps) {
  const [busy, setBusy] = useState<"successor" | "abandon" | null>(null);
  const [error, setError] = useState("");

  async function post(action: "successor" | "abandon") {
    setBusy(action);
    setError("");
    try {
      const res = await apiFetch(`/sessions/${sessionId}/${action}`, { method: "POST" });
      if (!res.ok) {
        throw new Error(`Request failed (${res.status})`);
      }
      window.location.reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : "The void refused.");
      setBusy(null);
    }
  }

  return (
    <div className="border border-doom-pink bg-doom-card p-6 text-center">
      <p className="font-display text-doom-pink text-xl tracking-wider mb-1">
        {characterName ?? "The wretch"} has perished
      </p>
      <p className="text-doom-ash text-sm mb-5">
        The world grinds on without them. Another doomed soul may take up the tale — the map,
        the chronicle, and the miseries remain.
      </p>
      {error && <p className="text-doom-pink text-sm mb-3">{error}</p>}
      <div className="flex justify-center gap-4">
        <button
          onClick={() => post("successor")}
          disabled={busy !== null}
          className="border border-doom-yellow text-doom-yellow px-4 py-2 text-sm uppercase tracking-wider hover:bg-doom-yellow/10 disabled:opacity-50"
        >
          {busy === "successor" ? "Digging a grave..." : "Roll a new wretch"}
        </button>
        <button
          onClick={() => post("abandon")}
          disabled={busy !== null}
          className="border border-doom-ash text-doom-ash px-4 py-2 text-sm uppercase tracking-wider hover:bg-doom-ash/10 disabled:opacity-50"
        >
          {busy === "abandon" ? "Turning away..." : "Abandon this world"}
        </button>
      </div>
    </div>
  );
}
