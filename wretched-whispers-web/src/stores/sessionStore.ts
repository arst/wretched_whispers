import { create } from "zustand";
import type { ChatMessageDto, ToolResultEvent, StateUpdateEvent } from "@/types/api";

export interface Message {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  authorName: string | null;
  toolResults: ToolResultEvent[];
}

interface SessionState {
  sessionId: string | null;
  status: string | null;
  messages: Message[];
  isStreaming: boolean;
  streamingMessageId: string | null;
  streamingText: string;
  error: string | null;

  // Actions
  setSession: (sessionId: string, status: string, messages: ChatMessageDto[]) => void;
  addPlayerMessage: (content: string) => void;
  startStreaming: () => void;
  appendNarrativeChunk: (text: string) => void;
  addToolResult: (result: ToolResultEvent) => void;
  setStateUpdate: (update: StateUpdateEvent) => void;
  finishStreaming: () => void;
  setError: (message: string) => void;
  clearError: () => void;
  reset: () => void;
}

function generateId(): string {
  return crypto.randomUUID();
}

export const useSessionStore = create<SessionState>()((set, get) => ({
  sessionId: null,
  status: null,
  messages: [],
  isStreaming: false,
  streamingMessageId: null,
  streamingText: "",
  error: null,

  setSession: (sessionId, status, dtos) =>
    set({
      sessionId,
      status,
      messages: dtos.map((dto) => ({
        id: generateId(),
        role: dto.role,
        content: dto.content ?? "",
        authorName: dto.authorName,
        toolResults: [],
      })),
      isStreaming: false,
      streamingMessageId: null,
      streamingText: "",
      error: null,
    }),

  addPlayerMessage: (content) =>
    set((state) => ({
      messages: [
        ...state.messages,
        {
          id: generateId(),
          role: "user" as const,
          content,
          authorName: null,
          toolResults: [],
        },
      ],
    })),

  startStreaming: () => {
    const id = generateId();
    set((state) => ({
      isStreaming: true,
      streamingMessageId: id,
      streamingText: "",
      error: null,
      messages: [
        ...state.messages,
        {
          id,
          role: "assistant" as const,
          content: "",
          authorName: "Game Master",
          toolResults: [],
        },
      ],
    }));
  },

  appendNarrativeChunk: (text) =>
    set((state) => ({
      streamingText: state.streamingText + text,
    })),

  addToolResult: (result) => {
    const { streamingMessageId } = get();
    if (!streamingMessageId) return;
    set((state) => ({
      messages: state.messages.map((msg) =>
        msg.id === streamingMessageId
          ? { ...msg, toolResults: [...msg.toolResults, result] }
          : msg
      ),
    }));
  },

  setStateUpdate: (update) =>
    set({ status: update.status }),

  finishStreaming: () => {
    const { streamingMessageId, streamingText } = get();
    if (!streamingMessageId) {
      set({ isStreaming: false, streamingText: "" });
      return;
    }
    set((state) => ({
      isStreaming: false,
      streamingMessageId: null,
      streamingText: "",
      messages: state.messages.map((msg) =>
        msg.id === streamingMessageId
          ? { ...msg, content: streamingText }
          : msg
      ),
    }));
  },

  setError: (message) =>
    set({ error: message }),

  clearError: () =>
    set({ error: null }),

  reset: () =>
    set({
      sessionId: null,
      status: null,
      messages: [],
      isStreaming: false,
      streamingMessageId: null,
      streamingText: "",
      error: null,
    }),
}));
