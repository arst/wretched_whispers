"use client";

import { useRef, useEffect, useCallback } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import { apiFetch } from "@/lib/api";
import type {
  NarrativeEvent,
  ToolResultEvent,
  TurnDeltaEvent,
  StateUpdateEvent,
  SseErrorEvent,
} from "@/types/api";

interface SseMessage {
  event: string;
  data: string;
  id?: number;
}

export function parseSseMessage(raw: string): SseMessage | null {
  let event = "message";
  const data: string[] = [];
  let id: number | undefined;

  for (const line of raw.split(/\r?\n/)) {
    if (line.startsWith("event:")) {
      event = line.slice(6).trim();
    } else if (line.startsWith("id:")) {
      const parsed = Number(line.slice(3).trim());
      if (Number.isSafeInteger(parsed)) id = parsed;
    } else if (line.startsWith("data:")) {
      data.push(line.slice(5).trimStart());
    }
  }

  return data.length > 0
    ? { event, data: data.join("\n"), ...(id === undefined ? {} : { id }) }
    : null;
}

export async function readSse(
  response: Response,
  onMessage: (message: SseMessage) => "done" | "error" | undefined,
  signal: AbortSignal
): Promise<"done" | "error" | "eof"> {
  if (!response.body) throw new Error("SSE response has no body");

  const reader = response.body
    .pipeThrough(new TextDecoderStream())
    .getReader();
  let buffer = "";

  while (!signal.aborted) {
    const { value, done } = await reader.read();
    if (done) break;

    buffer += value;
    const messages = buffer.split(/\r?\n\r?\n/);
    buffer = messages.pop() ?? "";

    for (const raw of messages) {
      const message = parseSseMessage(raw);
      if (message) {
        const outcome = onMessage(message);
        if (outcome) return outcome;
      }
    }
  }

  const message = parseSseMessage(buffer);
  if (message) {
    const outcome = onMessage(message);
    if (outcome) return outcome;
  }

  if (signal.aborted) {
    throw new DOMException("Aborted", "AbortError");
  }

  return "eof";
}

function handleMessage(ev: SseMessage): "done" | "error" | undefined {
  const s = useSessionStore.getState();

  switch (ev.event) {
    case "narrative": {
      const data: NarrativeEvent = JSON.parse(ev.data);
      s.appendNarrativeChunk(data.text);
      break;
    }
    case "tool_result": {
      const data: ToolResultEvent = JSON.parse(ev.data);
      s.addToolResult(data);
      break;
    }
    case "turn_delta": {
      const data: TurnDeltaEvent = JSON.parse(ev.data);
      s.setTurnDelta(data);
      break;
    }
    case "state_update": {
      const data: StateUpdateEvent = JSON.parse(ev.data);
      s.setStateUpdate(data);
      break;
    }
    case "done":
      s.finishStreaming();
      return "done";
    case "error": {
      const data: SseErrorEvent = JSON.parse(ev.data);
      s.setError(data.message);
      s.failStreaming();
      return "error";
    }
  }
}

export function useSseStream(sessionId: string) {
  const abortRef = useRef<AbortController | null>(null);
  const lastEventIdRef = useRef(0);

  useEffect(() => {
    return () => {
      abortRef.current?.abort();
    };
  }, []);

  const sendAction = useCallback(
    async (message: string, { silent = false }: { silent?: boolean } = {}) => {
      const store = useSessionStore.getState();

      if (store.isStreaming) return;

      abortRef.current?.abort();
      const ctrl = new AbortController();
      abortRef.current = ctrl;

      if (message.trim() && !silent) {
        store.addPlayerMessage(message);
      }
      store.startStreaming();

      try {
        const requestId = crypto.randomUUID();
        const response = await apiFetch(`/sessions/${sessionId}/turns`, {
          method: "POST",
          body: JSON.stringify({ requestId, message }),
          signal: ctrl.signal,
        });
        if (!response.ok) {
          throw new Error(`Turn submission failed (${response.status})`);
        }

        const turn: { turnId: string } = await response.json();
        lastEventIdRef.current = 0;
        let outcome: "done" | "error" | "eof" = "eof";
        while (!ctrl.signal.aborted && outcome === "eof") {
          const stream = await apiFetch(`/turns/${turn.turnId}/events`, {
            headers: lastEventIdRef.current
              ? { "Last-Event-ID": String(lastEventIdRef.current) }
              : undefined,
            signal: ctrl.signal,
          });
          if (!stream.ok) throw new Error(`SSE connection failed (${stream.status})`);
          outcome = await readSse(stream, (event) => {
            if (event.id !== undefined) {
              if (event.id <= lastEventIdRef.current) return;
              lastEventIdRef.current = event.id;
            }
            return handleMessage(event);
          }, ctrl.signal);
        }

        if (outcome === "eof") {
          throw new Error("SSE connection closed before the turn completed");
        }
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") {
          return;
        }
        const s = useSessionStore.getState();
        s.failStreaming();
        if (!s.error) {
          s.setError("Connection to the narrator was lost.");
        }
      }
    },
    [sessionId]
  );

  return { sendAction };
}
