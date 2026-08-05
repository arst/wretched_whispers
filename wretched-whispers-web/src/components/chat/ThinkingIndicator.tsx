"use client";

import { useSessionStore } from "@/stores/sessionStore";
import Dots from "@/components/ui/Dots";

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
      <div className="py-1">
        <Dots />
      </div>
    </div>
  );
}
