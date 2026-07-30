// Session types — match backend DTOs (camelCase from System.Text.Json)

export type Difficulty = "StoryMode" | "Grim" | "Doomed" | "Hardcore";

// Mirrors the backend CharacterClass enum, which string-serializes with its member names.
export type CharacterClass =
  | "Classless"
  | "FangedDeserter"
  | "GutterbornScum"
  | "EsotericHermit"
  | "OccultHerbmaster"
  | "HereticalPriest"
  | "CursedSkinwalker";

export interface SessionPreviewDto {
  sessionId: string;
  campaignName: string;
  description: string;
  characterName: string | null;
  characterClass: string | null;
  currentHp: number | null;
  maxHp: number | null;
  status: "character-creation" | "in-progress" | "ended" | "fallen";
  difficulty: Difficulty;
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
  difficulty: Difficulty;
  messages: ChatMessageDto[];
  totalMessages: number;
  page: number;
  pageSize: number;
  state: StateUpdateEvent;
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
  | "turn_delta"
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

// Authoritative account of what THIS turn changed — a deterministic diff of committed domain state,
// never written by the model. Rendered beside the prose as the source of truth for the outcome; a
// purchase the narration invented but no tool applied arrives here as all-zero (isNoOp).
export interface TurnDeltaEvent {
  silverChange: number;
  hpChange: number;
  itemsAdded: string[];
  itemsRemoved: string[];
  hoursElapsed: number;
  strengthChange: number;
  agilityChange: number;
  presenceChange: number;
  toughnessChange: number;
  miseryChange: number;
  newAfflictions: string[];
  died: boolean;
  worldEnded: boolean;
}

export interface StateUpdateEvent {
  campaignId: string;
  currentDay: number;
  currentHour: number;
  characterId?: string;
  characterName?: string;
  // Absent for a classless wretch — the backend omits it rather than sending "Classless Scum".
  characterClass?: string | null;
  characterHp?: number;
  characterMaxHp?: number;
  characterStrength?: number;
  characterAgility?: number;
  characterPresence?: number;
  characterToughness?: number;
  characterWeapon?: string | null;
  characterArmor?: string | null;
  characterInventory?: string[];
  characterSilver?: number;
  miseryCount: number;
  status: "character-creation" | "in-progress" | "ended" | "fallen";
  // Injuries (from Plan 01 backend enrichment, per D-08)
  hasLostEye?: boolean;
  hasStabbedLung?: boolean;
  hasBrokenHand?: boolean;
  hasCrushedFoot?: boolean;
  hasSeveredArm?: boolean;
  hasSmashedFace?: boolean;
  // Status effects
  isInfected?: boolean;
  isDizzyFromMagic?: boolean;
  isEncumbered?: boolean;
  isDead?: boolean;
  // Equipment condition
  armorTier?: string;
  hasShield?: boolean;
  isShieldBroken?: boolean;
  // World state
  worldEnded?: boolean;
  currentLocationName?: string | null;
  characterOmens?: number;
  characterScrolls?: string[];
  miseryPsalms?: string[];
}

export interface CharacterData {
  name: string;
  class: string | null;
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
  silver: number | null;
  // Injuries
  hasLostEye: boolean;
  hasStabbedLung: boolean;
  hasBrokenHand: boolean;
  hasCrushedFoot: boolean;
  hasSeveredArm: boolean;
  hasSmashedFace: boolean;
  // Status effects
  isInfected: boolean;
  isDizzyFromMagic: boolean;
  isEncumbered: boolean;
  isDead: boolean;
  // Equipment condition
  armorTier: string;
  hasShield: boolean;
  isShieldBroken: boolean;
  // Powers
  omens: number;
  scrolls: string[];
}

export interface SseErrorEvent {
  message: string;
}

export interface JournalEntryDto {
  category: string;
  text: string;
  day: number;
  hour: number;
}

export interface FallenCharacterDto {
  name: string;
  dayDied: number;
}

export interface PoiDto {
  name: string;
  type: string;
  x: number;
  y: number;
  connectedTo: string | null;
}
