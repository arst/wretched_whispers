"use client";

import { useRef, useCallback } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import { useAutoScroll } from "@/hooks/useAutoScroll";
import NarratorMessage from "./NarratorMessage";
import PlayerMessage from "./PlayerMessage";
import ThinkingIndicator from "./ThinkingIndicator";
import LoadMoreButton from "./LoadMoreButton";

export default function ChatWindow() {
  // Select messages with a stable selector to avoid re-render on streamingText changes
  const messages = useSessionStore((s) => s.messages);
  const isStreaming = useSessionStore((s) => s.isStreaming);
  const streamingMessageId = useSessionStore((s) => s.streamingMessageId);
  const streamingText = useSessionStore((s) => s.streamingText);
  const sessionId = useSessionStore((s) => s.sessionId);
  const hasMoreMessages = useSessionStore((s) => s.hasMoreMessages);

  const { containerRef, bottomRef, isPrependRef } = useAutoScroll([
    messages.length,
    streamingText,
  ]);

  // Scroll preservation for load-more (prepend) operations
  const prevScrollHeightRef = useRef(0);

  const handleBeforeLoadMore = useCallback(() => {
    const container = containerRef.current;
    if (container) {
      prevScrollHeightRef.current = container.scrollHeight;
      isPrependRef.current = true; // Prevent auto-scroll on this update
    }
  }, [containerRef, isPrependRef]);

  const handleLoadMoreComplete = useCallback(() => {
    const container = containerRef.current;
    if (!container) return;
    requestAnimationFrame(() => {
      container.scrollTop += container.scrollHeight - prevScrollHeightRef.current;
    });
  }, [containerRef]);

  return (
    <div
      ref={containerRef}
      className="flex-1 overflow-y-auto px-4 pb-24 pt-4"
    >
      <div className="mx-auto w-full md:w-[80vw] max-w-6xl">
        {sessionId && hasMoreMessages && (
          <LoadMoreButton
            sessionId={sessionId}
            onBeforeLoad={handleBeforeLoadMore}
            onLoadComplete={handleLoadMoreComplete}
          />
        )}

        {messages.map((msg) => {
          if (msg.role === "assistant") {
            const isCurrentlyStreaming =
              isStreaming && msg.id === streamingMessageId;
            return (
              <NarratorMessage
                key={msg.id}
                content={msg.content}
                toolResults={msg.toolResults}
                turnDelta={msg.turnDelta}
                isStreaming={isCurrentlyStreaming}
              />
            );
          }

          if (msg.role === "user") {
            return <PlayerMessage key={msg.id} content={msg.content} />;
          }

          // System messages: render as subtle notice
          if (msg.role === "system") {
            return (
              <p
                key={msg.id}
                className="text-doom-ash text-sm text-center italic mb-4 py-1"
              >
                {msg.content}
              </p>
            );
          }

          return null;
        })}

        <ThinkingIndicator />

        {/* Bottom sentinel for auto-scroll */}
        <div ref={bottomRef} />
      </div>
    </div>
  );
}
