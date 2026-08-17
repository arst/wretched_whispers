"use client";

import { useSessionStore } from "@/stores/sessionStore";
import ToolResultCallout from "./ToolResultCallout";
import TurnLedger from "./TurnLedger";
import type { ToolResultEvent, TurnDeltaEvent } from "@/types/api";

interface NarratorMessageProps {
  content: string;
  toolResults: ToolResultEvent[];
  turnDelta: TurnDeltaEvent | null;
  isStreaming?: boolean;
}

export default function NarratorMessage({
  content,
  toolResults,
  turnDelta,
  isStreaming,
}: NarratorMessageProps) {
  // When streaming, read from the dedicated streamingText field
  // to avoid re-rendering the entire message list
  const streamingText = useSessionStore((s) =>
    isStreaming ? s.streamingText : null,
  );

  const displayText = isStreaming ? (streamingText ?? "") : content;

  return (
    <div className="bg-doom-card border-doom-yellow mb-4 rounded border-l-2 px-5 py-4">
      <p className="text-doom-yellow font-body mb-2 text-sm font-semibold tracking-widest uppercase">
        Game Master
      </p>
      <div className="text-doom-bone text-xl leading-relaxed whitespace-pre-wrap">
        {displayText}
        {isStreaming && (
          <span className="bg-doom-yellow ml-0.5 inline-block h-4 w-0.5 animate-pulse align-text-bottom" />
        )}
      </div>
      {toolResults.length > 0 && (
        // Collapsed by default so the raw tool calls don't overload the player — the "This turn"
        // ledger below already carries the outcome. Native <details> = expand on click, no JS state.
        <details className="group mt-3">
          <summary className="text-doom-yellow/60 hover:text-doom-yellow cursor-pointer list-none text-xs tracking-wider uppercase select-none">
            <span className="inline-block transition-transform group-open:rotate-90">
              ▸
            </span>{" "}
            {toolResults.length} tool{" "}
            {toolResults.length === 1 ? "call" : "calls"}
          </summary>
          <div className="mt-2 flex flex-col gap-2">
            {toolResults.map((tr, i) => (
              <ToolResultCallout key={i} toolResult={tr} />
            ))}
          </div>
        </details>
      )}
      {turnDelta && (
        <div className="mt-3">
          <TurnLedger delta={turnDelta} />
        </div>
      )}
    </div>
  );
}
