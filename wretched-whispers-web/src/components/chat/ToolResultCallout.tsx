"use client";

import type { ToolResultEvent } from "@/types/api";

interface ToolResultCalloutProps {
  toolResult: ToolResultEvent;
}

function formatResult(result: unknown): string {
  if (typeof result === "string") return result;
  if (typeof result === "number" || typeof result === "boolean")
    return String(result);
  try {
    return JSON.stringify(result, null, 2);
  } catch {
    return String(result);
  }
}

function isDiceFunction(fn: string): boolean {
  const lower = fn.toLowerCase();
  return (
    lower.includes("dice") ||
    lower.includes("roll") ||
    lower.includes("d6") ||
    lower.includes("d20") ||
    lower.includes("d4") ||
    lower.includes("d8") ||
    lower.includes("d10") ||
    lower.includes("d12")
  );
}

export default function ToolResultCallout({
  toolResult,
}: ToolResultCalloutProps) {
  const isDice = isDiceFunction(toolResult.function);
  const resultText = formatResult(toolResult.result);

  return (
    <div className="border border-doom-yellow/60 bg-doom-dark rounded px-3 py-2">
      <p className="text-doom-yellow text-xs font-bold uppercase tracking-wider mb-1">
        {isDice ? "FATE DECIDES" : toolResult.function}
      </p>
      <p
        className={`text-sm leading-relaxed ${
          isDice ? "text-doom-yellow font-bold" : "text-doom-bone font-mono"
        } whitespace-pre-wrap`}
      >
        {resultText}
      </p>
    </div>
  );
}
