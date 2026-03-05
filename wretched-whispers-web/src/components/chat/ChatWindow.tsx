"use client";

import { useSessionStore } from "@/stores/sessionStore";
import { useAutoScroll } from "@/hooks/useAutoScroll";
import NarratorMessage from "./NarratorMessage";
import PlayerMessage from "./PlayerMessage";
import ThinkingIndicator from "./ThinkingIndicator";

export default function ChatWindow() {
  // Select messages with a stable selector to avoid re-render on streamingText changes
  const messages = useSessionStore((s) => s.messages);
  const isStreaming = useSessionStore((s) => s.isStreaming);
  const streamingMessageId = useSessionStore((s) => s.streamingMessageId);
  const streamingText = useSessionStore((s) => s.streamingText);

  const { containerRef, bottomRef, scrollToBottom: _scrollToBottom } = useAutoScroll([
    messages.length,
    streamingText,
  ]);

  return (
    <div
      ref={containerRef}
      className="flex-1 overflow-y-auto px-4 pb-24 pt-4"
    >
      <div className="max-w-2xl mx-auto">
        {messages.map((msg) => {
          if (msg.role === "assistant") {
            const isCurrentlyStreaming =
              isStreaming && msg.id === streamingMessageId;
            return (
              <NarratorMessage
                key={msg.id}
                content={msg.content}
                toolResults={msg.toolResults}
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
                className="text-doom-ash text-xs text-center italic mb-3 py-1"
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
