"use client";

import { useRef } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import { apiFetch } from "@/lib/api";

const PAGE_SIZE = 20;

interface LoadMoreButtonProps {
  sessionId: string;
  onBeforeLoad: () => void;
  onLoadComplete: () => void;
}

export default function LoadMoreButton({
  sessionId,
  onBeforeLoad,
  onLoadComplete,
}: LoadMoreButtonProps) {
  const hasMoreMessages = useSessionStore((s) => s.hasMoreMessages);
  const loadingMore = useSessionStore((s) => s.loadingMore);
  const messages = useSessionStore((s) => s.messages);
  const totalMessages = useSessionStore((s) => s.totalMessages);

  // Track which page of older messages to fetch next
  const nextPageRef = useRef<number | null>(null);

  // Initialize page ref: calculate the page number just before the currently loaded messages
  if (nextPageRef.current === null && totalMessages > 0) {
    const olderCount = totalMessages - messages.length;
    nextPageRef.current = olderCount > 0 ? Math.ceil(olderCount / PAGE_SIZE) : 0;
  }

  if (!hasMoreMessages) return null;

  async function handleLoadMore() {
    if (!nextPageRef.current || nextPageRef.current <= 0) return;

    const store = useSessionStore.getState();
    store.setLoadingMore(true);
    onBeforeLoad();

    try {
      const res = await apiFetch(
        `/sessions/${sessionId}/messages?page=${nextPageRef.current}&pageSize=${PAGE_SIZE}`
      );
      if (!res.ok) throw new Error("Failed to load messages");

      const data = await res.json();
      store.prependMessages(data.messages, data.totalMessages);
      nextPageRef.current = nextPageRef.current - 1;
      onLoadComplete();
    } catch {
      store.setLoadingMore(false);
    }
  }

  return (
    <div className="flex justify-center">
      <button
        type="button"
        onClick={handleLoadMore}
        disabled={loadingMore}
        aria-busy={loadingMore}
        className="min-h-[44px] text-doom-ash hover:text-doom-yellow transition-colors text-xs uppercase tracking-wider disabled:opacity-50"
      >
        {loadingMore ? (
          <span className="animate-pulse">Loading...</span>
        ) : (
          "Load earlier messages"
        )}
      </button>
    </div>
  );
}
