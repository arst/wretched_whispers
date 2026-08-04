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
}

export function parseSseMessage(raw: string): SseMessage | null {
  let event = "message";
  const data: string[] = [];

  for (const line of raw.split(/\r?\n/)) {
    if (line.startsWith("event:")) {
      event = line.slice(6).trim();
    } else if (line.startsWith("data:")) {
      data.push(line.slice(5).trimStart());
    }
  }

  return data.length > 0 ? { event, data: data.join("\n") } : null;
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

  useEffect(() => {
    return () => {
      abortRef.current?.abort();
    };
  }, []);

  const sendAction = useCallback(
    async (message: string, { silent = false }: { silent?: boolean } = {}) => {
      const store = useSessionStore.getState();

      if (store.isStreaming) return;

      // New attempt: clear any prior retry state so a stale failed message can't be resent.
      store.setFailedMessage(null);

      abortRef.current?.abort();
      const ctrl = new AbortController();
      abortRef.current = ctrl;

      if (message.trim() && !silent) {
        store.addPlayerMessage(message);
      }
      store.startStreaming();

      try {
        const response = await apiFetch(`/sessions/${sessionId}/actions`, {
          method: "POST",
          body: JSON.stringify({ message }),
          signal: ctrl.signal,
        });

        if (response.status === 409) {
          const s = useSessionStore.getState();
          s.setError("The narrator is still speaking...");
          s.failStreaming();
          return;
        }

        if (!response.ok) {
          throw new Error(`SSE connection failed (${response.status})`);
        }

        const outcome = await readSse(response, handleMessage, ctrl.signal);

        const s = useSessionStore.getState();
        if (outcome === "error") {
          // The stream delivered an error event (e.g. the model rate-limited). Remember this turn's
          // message so the player can retry it — the backend rolled the turn back, so it's safe.
          s.setFailedMessage(message);
        } else if (outcome === "eof") {
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
        s.setFailedMessage(message);
      }
    },
    [sessionId]
  );

  // Resend the last failed turn's message. Silent: the player bubble (if any) is already on screen,
  // and the backend discarded the failed turn, so this is a clean re-run rather than a duplicate.
  const retry = useCallback(() => {
    const store = useSessionStore.getState();
    const message = store.failedMessage;
    if (message === null) return;
    store.clearError();
    store.setFailedMessage(null);
    sendAction(message, { silent: true });
  }, [sendAction]);

  return { sendAction, retry };
}
