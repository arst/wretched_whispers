"use client";

import { useSessionStore } from "@/stores/sessionStore";

export default function ThinkingIndicator() {
  const isStreaming = useSessionStore((s) => s.isStreaming);
  const streamingText = useSessionStore((s) => s.streamingText);

  // Only show when streaming has started but no narrative text has arrived yet
  if (!isStreaming || streamingText.length > 0) return null;

  return (
    <div className="bg-doom-card border-l-2 border-doom-yellow rounded px-4 py-3 mb-3">
      <p className="text-doom-ash text-xs uppercase tracking-widest mb-2 font-body">
        Game Master
      </p>
      <div className="flex items-center gap-1.5 py-1">
        <span
          className="inline-block w-2 h-2 rounded-full bg-doom-yellow"
          style={{ animation: "doom-pulse 1.4s ease-in-out infinite" }}
        />
        <span
          className="inline-block w-2 h-2 rounded-full bg-doom-yellow"
          style={{
            animation: "doom-pulse 1.4s ease-in-out 0.2s infinite",
          }}
        />
        <span
          className="inline-block w-2 h-2 rounded-full bg-doom-yellow"
          style={{
            animation: "doom-pulse 1.4s ease-in-out 0.4s infinite",
          }}
        />
      </div>
    </div>
  );
}
