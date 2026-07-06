"use client";

import { useEffect, useState, useCallback, use } from "react";
import Link from "next/link";
import { apiFetch } from "@/lib/api";
import { useSessionStore } from "@/stores/sessionStore";
import { useSseStream } from "@/hooks/useSseStream";
import ChatWindow from "@/components/chat/ChatWindow";
import ChatInput from "@/components/chat/ChatInput";
import SplashScreen from "@/components/chat/SplashScreen";
import CharacterDrawer from "@/components/character/CharacterDrawer";
import EndCard from "@/components/session/EndCard";
import type { SessionDetailDto } from "@/types/api";

export default function GameSessionPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = use(params);

  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [showSplash, setShowSplash] = useState(false);
  const [splashDismissed, setSplashDismissed] = useState(false);

  const isStreaming = useSessionStore((s) => s.isStreaming);
  const error = useSessionStore((s) => s.error);
  const clearError = useSessionStore((s) => s.clearError);
  const status = useSessionStore((s) => s.status);
  const streamingText = useSessionStore((s) => s.streamingText);
  const reset = useSessionStore((s) => s.reset);
  const miseryCount = useSessionStore((s) => s.miseryCount);
  const worldEnded = useSessionStore((s) => s.worldEnded);
  const currentDay = useSessionStore((s) => s.currentDay);
  const characterData = useSessionStore((s) => s.characterData);

  const showEndCard = status === "ended" && !isStreaming;
  const isDead = characterData?.isDead ?? false;

  const { sendAction } = useSseStream(id);

  // Auto-dismiss error after 5 seconds
  useEffect(() => {
    if (!error) return;
    const timer = setTimeout(() => clearError(), 5000);
    return () => clearTimeout(timer);
  }, [error, clearError]);

  // Transition from splash when first narrative chunk arrives
  useEffect(() => {
    if (showSplash && streamingText.length > 0) {
      setShowSplash(false);
    }
  }, [showSplash, streamingText]);

  // Load session on mount
  useEffect(() => {
    let cancelled = false;

    async function loadSession() {
      try {
        const res = await apiFetch(`/sessions/${id}`);
        if (cancelled) return;

        if (res.status === 404) {
          setNotFound(true);
          setLoading(false);
          return;
        }

        if (!res.ok) {
          throw new Error(`Failed to load session (${res.status})`);
        }

        const data: SessionDetailDto = await res.json();
        const store = useSessionStore.getState();

        // If there are more messages than one page, load the LAST page
        // so the user sees the most recent messages first
        if (data.totalMessages > data.pageSize) {
          const lastPage = Math.ceil(data.totalMessages / data.pageSize);
          const latestRes = await apiFetch(`/sessions/${id}?page=${lastPage}&pageSize=${data.pageSize}`);
          if (cancelled) return;

          if (latestRes.ok) {
            const latestData: SessionDetailDto = await latestRes.json();
            store.setSession(latestData.sessionId, latestData.status, latestData.messages, latestData.totalMessages);
            store.setStateUpdate(latestData.state);
          } else {
            // Fallback to first page if last page request fails
            store.setSession(data.sessionId, data.status, data.messages, data.totalMessages);
            store.setStateUpdate(data.state);
          }
        } else {
          // Session fits in one page, use as-is
          store.setSession(data.sessionId, data.status, data.messages, data.totalMessages);
          store.setStateUpdate(data.state);
        }

        // New character-creation session with no messages: show splash and trigger narrator
        if (data.status === "character-creation" && data.messages.length === 0) {
          setShowSplash(true);
          setLoading(false);
          // Kick off the narrator's opening message (silent = no player bubble)
          sendAction("begin", { silent: true });
        } else {
          setLoading(false);
        }
      } catch (err) {
        if (cancelled) return;
        useSessionStore
          .getState()
          .setError(
            err instanceof Error
              ? err.message
              : "Failed to load session."
          );
        setLoading(false);
      }
    }

    loadSession();

    return () => {
      cancelled = true;
      reset();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const handleSplashTransition = useCallback(() => {
    setSplashDismissed(true);
  }, []);

  const handleSend = useCallback(
    (message: string) => {
      sendAction(message);
    },
    [sendAction]
  );

  // 404 state
  if (notFound) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen px-4">
        <h1 className="font-display text-doom-yellow text-3xl mb-4">
          Session Not Found
        </h1>
        <p className="text-doom-ash text-sm mb-8">
          This session has been consumed by the void.
        </p>
        <Link
          href="/sessions"
          className="text-doom-yellow text-sm uppercase tracking-wider hover:brightness-110 transition-all"
        >
          Return to Sessions
        </Link>
      </div>
    );
  }

  // Loading state
  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="flex items-center gap-2">
          <span
            className="inline-block w-2 h-2 rounded-full bg-doom-yellow"
            style={{ animation: "doom-pulse 1.4s ease-in-out infinite" }}
          />
          <span
            className="inline-block w-2 h-2 rounded-full bg-doom-yellow"
            style={{ animation: "doom-pulse 1.4s ease-in-out 0.2s infinite" }}
          />
          <span
            className="inline-block w-2 h-2 rounded-full bg-doom-yellow"
            style={{ animation: "doom-pulse 1.4s ease-in-out 0.4s infinite" }}
          />
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-screen pt-14">
      {/* Splash screen for new character creation sessions */}
      {showSplash && !splashDismissed && (
        <SplashScreen
          show={showSplash}
          onTransition={handleSplashTransition}
        />
      )}

      {/* Error banner */}
      {error && (
        <div className="bg-doom-pink/20 border-b border-doom-pink text-doom-pink text-sm text-center py-2 px-4">
          {error}
        </div>
      )}

      {/* Session status indicator */}
      {status === "character-creation" && (
        <div className="bg-doom-card border-b border-doom-yellow/30 text-doom-yellow text-xs text-center py-1.5 px-4 uppercase tracking-widest">
          Character Creation
        </div>
      )}

      {/* Character sheet drawer */}
      <CharacterDrawer />

      {/* Chat area */}
      <ChatWindow />

      {/* Input bar */}
      <ChatInput onSend={handleSend} disabled={isStreaming} status={status} />

      {/* End card overlay */}
      {showEndCard && characterData && (
        <EndCard
          characterName={characterData.name}
          isDead={isDead}
          worldEnded={worldEnded}
          miseryCount={miseryCount}
          currentDay={currentDay}
          onRestart={() => {}}
        />
      )}
    </div>
  );
}
