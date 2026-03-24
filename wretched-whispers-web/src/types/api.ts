// Session types — match backend DTOs (camelCase from System.Text.Json)

export interface SessionPreviewDto {
  sessionId: string;
  campaignName: string;
  description: string;
  characterName: string | null;
  currentHp: number | null;
  maxHp: number | null;
  status: "character-creation" | "in-progress" | "ended";
  lastPlayed: string | null;
}

export interface SessionDetailDto {
  sessionId: string;
  campaignId: string;
  campaignName: string;
  description: string;
  currentDay: number;
  currentHour: number;
  status: string;
  messages: ChatMessageDto[];
  totalMessages: number;
  page: number;
  pageSize: number;
  characterName?: string | null;
  characterHp?: number | null;
  characterMaxHp?: number | null;
  characterStrength?: number | null;
  characterAgility?: number | null;
  characterPresence?: number | null;
  characterToughness?: number | null;
  characterWeapon?: string | null;
  characterArmor?: string | null;
  characterInventory?: string[] | null;
}

export interface ChatMessageDto {
  role: "user" | "assistant" | "system";
  content: string | null;
  authorName: string | null;
}

export interface CreateSessionResponse {
  sessionId: string;
  campaignId: string;
}

// Auth types

export interface LoginResponse {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

// SSE event types

export type SseEventType =
  | "narrative"
  | "tool_result"
  | "state_update"
  | "done"
  | "error";

export interface NarrativeEvent {
  text: string;
}

export interface ToolResultEvent {
  function: string;
  result: unknown;
}

export interface StateUpdateEvent {
  campaignId: string;
  currentDay: number;
  currentHour: number;
  characterId?: string;
  characterName?: string;
  characterHp?: number;
  characterMaxHp?: number;
  characterStrength?: number;
  characterAgility?: number;
  characterPresence?: number;
  characterToughness?: number;
  characterWeapon?: string | null;
  characterArmor?: string | null;
  characterInventory?: string[];
  miseryCount: number;
  status: "character-creation" | "in-progress" | "ended";
}

export interface CharacterData {
  name: string;
  currentHp: number;
  maxHp: number;
  abilities: {
    strength: number;
    agility: number;
    presence: number;
    toughness: number;
  };
  weapon: string | null;
  armor: string | null;
  inventory: string[];
}

export interface SseErrorEvent {
  message: string;
}
