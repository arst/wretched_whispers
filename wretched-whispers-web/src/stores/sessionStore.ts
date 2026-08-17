import { create } from "zustand";
import type {
  ChatMessageDto,
  ToolResultEvent,
  TurnDeltaEvent,
  StateUpdateEvent,
} from "@/types/api";

export interface Message {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  authorName: string | null;
  toolResults: ToolResultEvent[];
  turnDelta: TurnDeltaEvent | null;
}

export type SessionDrawer = "character" | "journal" | "map";

interface SessionState {
  sessionId: string | null;
  status: string | null;
  messages: Message[];
  isStreaming: boolean;
  streamingMessageId: string | null;
  streamingText: string;
  error: string | null;
  // The last state_update, kept whole. Null until the campaign has a character; the optional
  // character* fields are populated exactly when it does.
  characterData: StateUpdateEvent | null;
  activeDrawer: SessionDrawer | null;
  totalMessages: number;
  hasMoreMessages: boolean;
  loadingMore: boolean;
  miseryCount: number;
  worldEnded: boolean;
  currentDay: number;
  currentLocationName: string | null;
  miseryPsalms: string[];

  // Actions
  setSession: (
    sessionId: string,
    status: string,
    messages: ChatMessageDto[],
    totalMessages?: number,
  ) => void;
  addPlayerMessage: (content: string) => void;
  startStreaming: () => void;
  appendNarrativeChunk: (text: string) => void;
  addToolResult: (result: ToolResultEvent) => void;
  setTurnDelta: (delta: TurnDeltaEvent) => void;
  setStateUpdate: (update: StateUpdateEvent) => void;
  finishStreaming: () => void;
  failStreaming: () => void;
  setError: (message: string) => void;
  clearError: () => void;
  reset: () => void;
  toggleDrawer: (drawer: SessionDrawer) => void;
  prependMessages: (msgs: ChatMessageDto[], total: number) => void;
  setLoadingMore: (loading: boolean) => void;
}

function message(
  role: Message["role"],
  content: string,
  authorName: string | null = null,
): Message {
  return {
    id: crypto.randomUUID(),
    role,
    content,
    authorName,
    toolResults: [],
    turnDelta: null,
  };
}

const initialState = {
  sessionId: null,
  status: null,
  messages: [],
  isStreaming: false,
  streamingMessageId: null,
  streamingText: "",
  error: null,
  characterData: null,
  activeDrawer: null,
  totalMessages: 0,
  hasMoreMessages: false,
  loadingMore: false,
  miseryCount: 0,
  worldEnded: false,
  currentDay: 1,
  currentLocationName: null,
  miseryPsalms: [],
} satisfies Partial<SessionState>;

export const useSessionStore = create<SessionState>()((set, get) => ({
  ...initialState,

  setSession: (sessionId, status, dtos, totalMessages = 0) =>
    set({
      ...initialState,
      sessionId,
      status,
      messages: dtos.map((dto) =>
        message(dto.role, dto.content ?? "", dto.authorName),
      ),
      totalMessages,
      hasMoreMessages: dtos.length < totalMessages,
    }),

  addPlayerMessage: (content) =>
    set((state) => ({
      messages: [...state.messages, message("user", content)],
    })),

  startStreaming: () => {
    const placeholder = message("assistant", "", "Game Master");
    set((state) => ({
      isStreaming: true,
      streamingMessageId: placeholder.id,
      streamingText: "",
      error: null,
      messages: [...state.messages, placeholder],
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
          : msg,
      ),
    }));
  },

  setTurnDelta: (delta) => {
    const { streamingMessageId } = get();
    if (!streamingMessageId) return;
    set((state) => ({
      messages: state.messages.map((msg) =>
        msg.id === streamingMessageId ? { ...msg, turnDelta: delta } : msg,
      ),
    }));
  },

  setStateUpdate: (update) =>
    set({
      status: update.status,
      miseryCount: update.miseryCount,
      worldEnded: update.worldEnded ?? false,
      currentDay: update.currentDay,
      currentLocationName: update.currentLocationName ?? null,
      miseryPsalms: update.miseryPsalms ?? [],
      // A character-less update (still in creation) leaves the previous sheet alone.
      ...(update.characterName && update.characterHp != null
        ? { characterData: update }
        : {}),
    }),

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
          : msg,
      ),
    }));
  },

  // A turn failed (429, dropped connection, server error). Drop the empty assistant placeholder the
  // failed turn left behind (keep it only if it managed to stream real text or a tool/delta) so the
  // chat shows just the player's message + the error banner. The backend rolls the turn back, so
  // nothing was persisted server-side.
  failStreaming: () => {
    const { streamingMessageId, streamingText } = get();
    set((state) => ({
      isStreaming: false,
      streamingMessageId: null,
      streamingText: "",
      messages: streamingMessageId
        ? state.messages
            .filter(
              (m) =>
                !(
                  m.id === streamingMessageId &&
                  streamingText.trim() === "" &&
                  m.toolResults.length === 0 &&
                  !m.turnDelta
                ),
            )
            .map((m) =>
              m.id === streamingMessageId
                ? { ...m, content: streamingText }
                : m,
            )
        : state.messages,
    }));
  },

  setError: (message) => set({ error: message }),

  clearError: () => set({ error: null }),

  reset: () => set(initialState),

  toggleDrawer: (drawer) =>
    set((state) => ({
      activeDrawer: state.activeDrawer === drawer ? null : drawer,
    })),

  prependMessages: (msgs, total) =>
    set((state) => ({
      messages: [
        ...msgs.map((dto) =>
          message(dto.role, dto.content ?? "", dto.authorName),
        ),
        ...state.messages,
      ],
      totalMessages: total,
      hasMoreMessages: state.messages.length + msgs.length < total,
      loadingMore: false,
    })),

  setLoadingMore: (loading) => set({ loadingMore: loading }),
}));
