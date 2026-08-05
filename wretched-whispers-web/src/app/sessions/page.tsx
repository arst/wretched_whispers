"use client";

import { useState, useEffect, useCallback } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, apiErrorMessage } from "@/lib/api";
import type { SessionPreviewDto, CreateSessionResponse } from "@/types/api";
import SessionCard from "@/components/session/SessionCard";
import Button from "@/components/ui/Button";
import WretchForm, { type WretchChoices } from "@/components/session/WretchForm";

export default function SessionsPage() {
  const [sessions, setSessions] = useState<SessionPreviewDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [pickerOpen, setPickerOpen] = useState(false);
  const router = useRouter();

  const loadSessions = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const res = await apiFetch("/sessions");
      if (!res.ok) {
        throw new Error(`Failed to load sessions (${res.status})`);
      }
      const data: SessionPreviewDto[] = await res.json();
      // Sort by lastPlayed descending (most recent first)
      data.sort((a, b) => {
        if (!a.lastPlayed && !b.lastPlayed) return 0;
        if (!a.lastPlayed) return 1;
        if (!b.lastPlayed) return -1;
        return new Date(b.lastPlayed).getTime() - new Date(a.lastPlayed).getTime();
      });
      setSessions(data);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "The void swallowed your sessions."
      );
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadSessions();
  }, [loadSessions]);

  async function handleCreateSession({ characterName, characterClass, difficulty }: WretchChoices) {
    setCreating(true);
    setError("");

    try {
      const res = await apiFetch("/sessions", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        // characterClass is omitted when null — that is how the domain is asked to roll one.
        body: JSON.stringify({
          characterName,
          difficulty,
          ...(characterClass ? { characterClass } : {}),
        }),
      });
      if (!res.ok) throw new Error(await apiErrorMessage(res, "Failed to create session"));
      const data: CreateSessionResponse = await res.json();
      router.push(`/sessions/play?id=${data.sessionId}`);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "The abyss refused your offering."
      );
      setCreating(false);
      setPickerOpen(false);
    }
  }

  return (
    <div className="max-w-2xl mx-auto px-4 pt-20 pb-8">
      <div className="flex items-center justify-between mb-8">
        <h1 className="font-display text-doom-yellow text-2xl tracking-wider">
          YOUR SESSIONS
        </h1>
        <Button
          variant="primary"
          onClick={() => setPickerOpen(true)}
        >
          New Session
        </Button>
      </div>

      {error && (
        <p className="text-doom-pink text-sm text-center mb-6">{error}</p>
      )}

      {loading ? (
        <div className="flex flex-col gap-4">
          {[1, 2, 3].map((i) => (
            <div
              key={i}
              className="bg-doom-card border border-doom-card h-28 animate-pulse"
            />
          ))}
        </div>
      ) : sessions.length === 0 ? (
        <p className="text-doom-ash text-center py-16">
          No sessions yet. The void awaits.
        </p>
      ) : (
        <div className="flex flex-col gap-4">
          {sessions.map((session) => (
            <SessionCard key={session.sessionId} session={session} />
          ))}
        </div>
      )}

      {pickerOpen && (
        <WretchForm
          title="FORGE A WRETCH"
          intro="The world is already dying. Say who walks into it."
          withDifficulty
          confirmLabel="Begin"
          onConfirm={handleCreateSession}
          onCancel={() => setPickerOpen(false)}
          busy={creating}
        />
      )}
    </div>
  );
}
