"use client";

import { useRef, useEffect, useCallback } from "react";
import { fetchEventSource } from "@microsoft/fetch-event-source";
import { useAuthStore } from "@/stores/authStore";
import { useSessionStore } from "@/stores/sessionStore";
import type {
  NarrativeEvent,
  ToolResultEvent,
  StateUpdateEvent,
  SseErrorEvent,
} from "@/types/api";

const API_URL = process.env.NEXT_PUBLIC_API_URL!;

export function useSseStream(sessionId: string) {
  const abortRef = useRef<AbortController | null>(null);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      abortRef.current?.abort();
    };
  }, []);

  const sendAction = useCallback(
    async (message: string, { silent = false }: { silent?: boolean } = {}) => {
      const { accessToken } = useAuthStore.getState();
      const store = useSessionStore.getState();

      // Prevent sending while already streaming
      if (store.isStreaming) return;

      // Abort any lingering connection
      abortRef.current?.abort();
      const ctrl = new AbortController();
      abortRef.current = ctrl;

      // Optimistic UI: add player message (skip for silent system kicks)
      if (message.trim() && !silent) {
        store.addPlayerMessage(message);
      }
      store.startStreaming();

      try {
        await fetchEventSource(
          `${API_URL}/sessions/${sessionId}/actions`,
          {
            method: "POST",
            headers: {
              Authorization: `Bearer ${accessToken}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify({ message }),
            signal: ctrl.signal,
            openWhenHidden: true,

            async onopen(response) {
              if (response.status === 409) {
                useSessionStore
                  .getState()
                  .setError("The narrator is still speaking...");
                useSessionStore.getState().finishStreaming();
                ctrl.abort();
                return;
              }
              if (!response.ok) {
                throw new Error(
                  `SSE connection failed (${response.status})`
                );
              }
            },

            onmessage(ev) {
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
                case "state_update": {
                  const data: StateUpdateEvent = JSON.parse(ev.data);
                  s.setStateUpdate(data);
                  break;
                }
                case "done": {
                  s.finishStreaming();
                  ctrl.abort();
                  break;
                }
                case "error": {
                  const data: SseErrorEvent = JSON.parse(ev.data);
                  s.setError(data.message);
                  s.finishStreaming();
                  ctrl.abort();
                  break;
                }
              }
            },

            onerror(err) {
              // On any error, finish streaming and stop retrying
              useSessionStore.getState().finishStreaming();
              ctrl.abort();
              throw err;
            },

            onclose() {
              // Server closed the connection
              const s = useSessionStore.getState();
              if (s.isStreaming) {
                s.finishStreaming();
              }
            },
          }
        );
      } catch (err) {
        // AbortError is expected when we call ctrl.abort()
        if (err instanceof DOMException && err.name === "AbortError") {
          return;
        }
        const s = useSessionStore.getState();
        if (s.isStreaming) {
          s.finishStreaming();
        }
        if (!s.error) {
          s.setError("Connection to the narrator was lost.");
        }
      }
    },
    [sessionId]
  );

  return { sendAction };
}
