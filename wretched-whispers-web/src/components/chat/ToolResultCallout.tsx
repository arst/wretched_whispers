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

interface DiceRollData {
  formula: string;
  result: number;
}

function isDiceRollData(result: unknown): result is DiceRollData {
  return (
    typeof result === "object" &&
    result !== null &&
    "formula" in result &&
    "result" in result
  );
}

const DICE_FUNCTION = /dice|roll|d(4|6|8|10|12|20)/i;

export default function ToolResultCallout({
  toolResult,
}: ToolResultCalloutProps) {
  const isDice = DICE_FUNCTION.test(toolResult.function);
  const hasStructuredDice = isDice && isDiceRollData(toolResult.result);

  if (hasStructuredDice) {
    const diceData = toolResult.result as DiceRollData;
    return (
      <div className="border-doom-yellow/60 bg-doom-dark rounded border px-3 py-2">
        <p className="text-doom-yellow mb-1 text-xs font-bold tracking-wider uppercase">
          FATE DECIDES
        </p>
        <p className="font-mono text-xs text-[#8a8a8a]">{diceData.formula}</p>
        <p className="font-display text-lg font-bold text-[#ffe000]">
          = {diceData.result}
        </p>
      </div>
    );
  }

  const resultText = formatResult(toolResult.result);

  return (
    <div className="border-doom-yellow/60 bg-doom-dark rounded border px-3 py-2">
      <p className="text-doom-yellow mb-1 text-xs font-bold tracking-wider uppercase">
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
