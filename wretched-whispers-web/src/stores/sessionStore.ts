import { create } from "zustand";
import type { ChatMessageDto, ToolResultEvent, StateUpdateEvent, CharacterData } from "@/types/api";

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
  characterData: CharacterData | null;
  drawerOpen: boolean;
  totalMessages: number;
  currentPage: number;
  hasMoreMessages: boolean;
  loadingMore: boolean;
  miseryCount: number;
  worldEnded: boolean;
  currentDay: number;

  // Actions
  setSession: (sessionId: string, status: string, messages: ChatMessageDto[], totalMessages?: number) => void;
  addPlayerMessage: (content: string) => void;
  startStreaming: () => void;
  appendNarrativeChunk: (text: string) => void;
  addToolResult: (result: ToolResultEvent) => void;
  setStateUpdate: (update: StateUpdateEvent) => void;
  finishStreaming: () => void;
  setError: (message: string) => void;
  clearError: () => void;
  reset: () => void;
  setCharacterData: (data: CharacterData) => void;
  toggleDrawer: () => void;
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
  characterData: null,
  drawerOpen: false,
  totalMessages: 0,
  currentPage: 1,
  hasMoreMessages: false,
  loadingMore: false,
  miseryCount: 0,
  worldEnded: false,
  currentDay: 1,

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
      })),
      totalMessages,
      hasMoreMessages: dtos.length < totalMessages,
      currentPage: 1,
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

  setStateUpdate: (update) => {
    const newState: Partial<SessionState> = {
      status: update.status,
      miseryCount: update.miseryCount,
      worldEnded: update.worldEnded ?? false,
      currentDay: update.currentDay,
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
      characterData: null,
      drawerOpen: false,
      totalMessages: 0,
      currentPage: 1,
      hasMoreMessages: false,
      loadingMore: false,
      miseryCount: 0,
      worldEnded: false,
      currentDay: 1,
    }),

  setCharacterData: (data) => set({ characterData: data }),

  toggleDrawer: () => set((s) => ({ drawerOpen: !s.drawerOpen })),

  prependMessages: (msgs, total) =>
    set((state) => ({
      messages: [
        ...msgs.map((dto) => ({
          id: generateId(),
          role: dto.role,
          content: dto.content ?? "",
          authorName: dto.authorName,
          toolResults: [],
        })),
        ...state.messages,
      ],
      totalMessages: total,
      hasMoreMessages: state.messages.length + msgs.length < total,
      loadingMore: false,
    })),

  setLoadingMore: (loading) => set({ loadingMore: loading }),
}));
