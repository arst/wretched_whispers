import { create } from "zustand";
import type { ChatMessageDto, ToolResultEvent, TurnDeltaEvent, StateUpdateEvent, CharacterData } from "@/types/api";

export interface Message {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  authorName: string | null;
  toolResults: ToolResultEvent[];
  turnDelta: TurnDeltaEvent | null;
}

interface SessionState {
  sessionId: string | null;
  status: string | null;
  messages: Message[];
  isStreaming: boolean;
  streamingMessageId: string | null;
  streamingText: string;
  error: string | null;
  failedMessage: string | null;
  characterData: CharacterData | null;
  drawerOpen: boolean;
  journalOpen: boolean;
  mapOpen: boolean;
  totalMessages: number;
  currentPage: number;
  hasMoreMessages: boolean;
  loadingMore: boolean;
  miseryCount: number;
  worldEnded: boolean;
  currentDay: number;
  currentLocationName: string | null;
  miseryPsalms: string[];

  // Actions
  setSession: (sessionId: string, status: string, messages: ChatMessageDto[], totalMessages?: number) => void;
  addPlayerMessage: (content: string) => void;
  startStreaming: () => void;
  appendNarrativeChunk: (text: string) => void;
  addToolResult: (result: ToolResultEvent) => void;
  setTurnDelta: (delta: TurnDeltaEvent) => void;
  setStateUpdate: (update: StateUpdateEvent) => void;
  finishStreaming: () => void;
  failStreaming: () => void;
  setFailedMessage: (message: string | null) => void;
  setError: (message: string) => void;
  clearError: () => void;
  reset: () => void;
  toggleDrawer: () => void;
  toggleJournal: () => void;
  toggleMap: () => void;
  prependMessages: (msgs: ChatMessageDto[], total: number) => void;
  setLoadingMore: (loading: boolean) => void;
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
  failedMessage: null,
  characterData: null,
  drawerOpen: false,
  journalOpen: false,
  mapOpen: false,
  totalMessages: 0,
  currentPage: 1,
  hasMoreMessages: false,
  loadingMore: false,
  miseryCount: 0,
  worldEnded: false,
  currentDay: 1,
  currentLocationName: null,
  miseryPsalms: [],

  setSession: (sessionId, status, dtos, totalMessages = 0) =>
    set({
      sessionId,
      status,
      messages: dtos.map((dto) => ({
        id: generateId(),
        role: dto.role,
        content: dto.content ?? "",
        authorName: dto.authorName,
        toolResults: [],
        turnDelta: null,
      })),
      totalMessages,
      hasMoreMessages: dtos.length < totalMessages,
      currentPage: 1,
      isStreaming: false,
      streamingMessageId: null,
      streamingText: "",
      error: null,
      failedMessage: null,
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
          turnDelta: null,
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
          turnDelta: null,
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

  setTurnDelta: (delta) => {
    const { streamingMessageId } = get();
    if (!streamingMessageId) return;
    set((state) => ({
      messages: state.messages.map((msg) =>
        msg.id === streamingMessageId ? { ...msg, turnDelta: delta } : msg
      ),
    }));
  },

  setStateUpdate: (update) => {
    const newState: Partial<SessionState> = {
      status: update.status,
      miseryCount: update.miseryCount,
      worldEnded: update.worldEnded ?? false,
      currentDay: update.currentDay,
      currentLocationName: update.currentLocationName ?? null,
      miseryPsalms: update.miseryPsalms ?? [],
    };

    if (update.characterName && update.characterHp != null) {
      newState.characterData = {
        name: update.characterName,
        currentHp: update.characterHp,
        maxHp: update.characterMaxHp!,
        abilities: {
          strength: update.characterStrength ?? 0,
          agility: update.characterAgility ?? 0,
          presence: update.characterPresence ?? 0,
          toughness: update.characterToughness ?? 0,
        },
        weapon: update.characterWeapon ?? null,
        armor: update.characterArmor ?? null,
        inventory: update.characterInventory ?? [],
        silver: update.characterSilver ?? null,
        // Injuries
        hasLostEye: update.hasLostEye ?? false,
        hasStabbedLung: update.hasStabbedLung ?? false,
        hasBrokenHand: update.hasBrokenHand ?? false,
        hasCrushedFoot: update.hasCrushedFoot ?? false,
        hasSeveredArm: update.hasSeveredArm ?? false,
        hasSmashedFace: update.hasSmashedFace ?? false,
        // Status effects
        isInfected: update.isInfected ?? false,
        isDizzyFromMagic: update.isDizzyFromMagic ?? false,
        isEncumbered: update.isEncumbered ?? false,
        isDead: update.isDead ?? false,
        // Equipment condition
        armorTier: update.armorTier ?? "none",
        hasShield: update.hasShield ?? false,
        isShieldBroken: update.isShieldBroken ?? false,
        // Powers
        omens: update.characterOmens ?? 0,
        scrolls: update.characterScrolls ?? [],
      };
    }

    set(newState);
  },

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

  // A turn failed (429, dropped connection, server error). Drop the empty assistant placeholder the
  // failed turn left behind (keep it only if it managed to stream real text or a tool/delta) so the
  // chat shows just the player's message + the retry banner. The player message stays so retry can
  // resend it silently. The backend rolls the turn back, so nothing was persisted server-side.
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
                )
            )
            .map((m) =>
              m.id === streamingMessageId ? { ...m, content: streamingText } : m
            )
        : state.messages,
    }));
  },

  setFailedMessage: (message) =>
    set({ failedMessage: message }),

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
      failedMessage: null,
      characterData: null,
      drawerOpen: false,
      journalOpen: false,
      mapOpen: false,
      totalMessages: 0,
      currentPage: 1,
      hasMoreMessages: false,
      loadingMore: false,
      miseryCount: 0,
      worldEnded: false,
      currentDay: 1,
      currentLocationName: null,
      miseryPsalms: [],
    }),

  toggleDrawer: () => set((s) => ({ drawerOpen: !s.drawerOpen })),

  toggleJournal: () => set((s) => ({ journalOpen: !s.journalOpen })),

  toggleMap: () => set((s) => ({ mapOpen: !s.mapOpen })),

  prependMessages: (msgs, total) =>
    set((state) => ({
      messages: [
        ...msgs.map((dto) => ({
          id: generateId(),
          role: dto.role,
          content: dto.content ?? "",
          authorName: dto.authorName,
          toolResults: [],
          turnDelta: null,
        })),
        ...state.messages,
      ],
      totalMessages: total,
      hasMoreMessages: state.messages.length + msgs.length < total,
      loadingMore: false,
    })),

  setLoadingMore: (loading) => set({ loadingMore: loading }),
}));
