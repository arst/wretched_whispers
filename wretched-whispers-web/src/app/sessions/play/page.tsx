"use client";

import { Suspense, useEffect, useState, useCallback } from "react";
import { useSearchParams } from "next/navigation";
import Link from "next/link";
import { apiFetch } from "@/lib/api";
import { useSessionStore } from "@/stores/sessionStore";
import { useSseStream } from "@/hooks/useSseStream";
import ChatWindow from "@/components/chat/ChatWindow";
import ChatInput from "@/components/chat/ChatInput";
import SplashScreen from "@/components/chat/SplashScreen";
import CharacterDrawer from "@/components/character/CharacterDrawer";
import JournalDrawer from "@/components/journal/JournalDrawer";
import MapDrawer from "@/components/map/MapDrawer";
import EndCard from "@/components/session/EndCard";
import DeathPanel from "@/components/session/DeathPanel";
import type { SessionDetailDto } from "@/types/api";

// Session id comes from the ?id= query string rather than a dynamic route segment, so the app
// static-exports cleanly for the desktop build (Next's output:export has no runtime dynamic routes).
function LoadingDots() {
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

function GameSession({ id }: { id: string }) {
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
  const failedMessage = useSessionStore((s) => s.failedMessage);

  const showEndCard = status === "ended" && !isStreaming;
  const showDeathPanel = status === "fallen" && !isStreaming;
  const isDead = characterData?.isDead ?? false;

  const { sendAction, retry } = useSseStream(id);

  // Auto-dismiss transient errors after 5s — but keep a retryable failure on screen so the player
  // can act on it.
  useEffect(() => {
    if (!error || failedMessage) return;
    const timer = setTimeout(() => clearError(), 5000);
    return () => clearTimeout(timer);
  }, [error, failedMessage, clearError]);

  // Transition from splash when first narrative chunk arrives
  useEffect(() => {
    if (showSplash && streamingText.length > 0) {
      setShowSplash(false);
    }
  }, [showSplash, streamingText]);

  // If the opening turn fails (e.g. rate-limited), drop the splash so the retry banner is reachable.
  useEffect(() => {
    if (showSplash && error) {
      setShowSplash(false);
    }
  }, [showSplash, error]);

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

        // An empty chronicle needs its opening narration. This covers both a brand-new session and a
        // successor's fresh chronicle — the character already exists in both cases, so the emptiness of
        // the chronicle is the signal, not the session status.
        if (data.messages.length === 0 && data.status !== "ended") {
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
    return <LoadingDots />;
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

      {/* Error banner — with a Retry when the failed turn can be resent */}
      {error && (
        <div className="bg-doom-pink/20 border-b border-doom-pink text-doom-pink text-sm py-2 px-4 flex items-center justify-center gap-3">
          <span>{error}</span>
          {failedMessage && (
            <button
              onClick={retry}
              disabled={isStreaming}
              className="uppercase tracking-wider text-xs border border-doom-pink px-2 py-0.5 hover:bg-doom-pink hover:text-doom-black transition-colors disabled:opacity-40 cursor-pointer"
            >
              Retry
            </button>
          )}
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

      {/* Campaign journal drawer */}
      <JournalDrawer />

      {/* Regional map drawer */}
      <MapDrawer />

      {/* Chat area */}
      <ChatWindow />

      {/* Input bar */}
      {showDeathPanel ? (
        <DeathPanel sessionId={id} characterName={characterData?.name ?? null} />
      ) : (
        <ChatInput onSend={handleSend} disabled={isStreaming} status={status} />
      )}

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

function GameSessionFromQuery() {
  const id = useSearchParams().get("id") ?? "";
  return <GameSession id={id} />;
}

export default function GameSessionPage() {
  // useSearchParams requires a Suspense boundary under static export.
  return (
    <Suspense fallback={<LoadingDots />}>
      <GameSessionFromQuery />
    </Suspense>
  );
}
