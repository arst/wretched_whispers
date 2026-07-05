"use client";

import { useSessionStore } from "@/stores/sessionStore";
import ToolResultCallout from "./ToolResultCallout";
import type { ToolResultEvent } from "@/types/api";

interface NarratorMessageProps {
  content: string;
  toolResults: ToolResultEvent[];
  isStreaming?: boolean;
}

export default function NarratorMessage({
  content,
  toolResults,
  isStreaming,
}: NarratorMessageProps) {
  // When streaming, read from the dedicated streamingText field
  // to avoid re-rendering the entire message list
  const streamingText = useSessionStore((s) =>
    isStreaming ? s.streamingText : null
  );

  const displayText = isStreaming ? (streamingText ?? "") : content;

  return (
    <div className="bg-doom-card border-l-2 border-doom-yellow rounded px-5 py-4 mb-4">
      <p className="text-doom-yellow text-sm uppercase tracking-widest mb-2 font-body font-semibold">
        Game Master
      </p>
      <div className="text-doom-bone text-xl leading-relaxed whitespace-pre-wrap">
        {displayText}
        {isStreaming && (
          <span className="inline-block w-0.5 h-4 bg-doom-yellow ml-0.5 animate-pulse align-text-bottom" />
        )}
      </div>
      {toolResults.length > 0 && (
        <div className="mt-3 flex flex-col gap-2">
          {toolResults.map((tr, i) => (
            <ToolResultCallout key={i} toolResult={tr} />
          ))}
        </div>
      )}
    </div>
  );
}
