import { beforeEach, describe, expect, it } from "vitest";
import { useSessionStore } from "./sessionStore";
import type { StateUpdateEvent } from "@/types/api";

const update = (extra: Partial<StateUpdateEvent> = {}): StateUpdateEvent => ({
  campaignId: "c",
  currentDay: 1,
  currentHour: 1,
  miseryCount: 0,
  status: "in-progress",
  ...extra,
});

describe("session store", () => {
  beforeEach(() => useSessionStore.getState().reset());

  // A state_update sent before the wretch exists (or during creation) carries no character fields.
  // Blanking the sheet on one would empty the drawer mid-session.
  it("keeps the last character sheet through a character-less update", () => {
    const store = useSessionStore.getState();
    store.setStateUpdate(
      update({ characterName: "Grim", characterHp: 5, characterMaxHp: 8 }),
    );
    store.setStateUpdate(update({ currentDay: 2 }));

    const state = useSessionStore.getState();
    expect(state.characterData?.characterName).toBe("Grim");
    expect(state.currentDay).toBe(2);
  });

  it("keeps only one drawer active", () => {
    const store = useSessionStore.getState();
    store.toggleDrawer("character");
    store.toggleDrawer("map");
    expect(useSessionStore.getState().activeDrawer).toBe("map");

    useSessionStore.getState().toggleDrawer("map");
    expect(useSessionStore.getState().activeDrawer).toBeNull();
  });

  it("preserves partial narrative when a stream fails", () => {
    const store = useSessionStore.getState();
    store.startStreaming();
    store.appendNarrativeChunk("A partial warning");
    store.failStreaming();

    const state = useSessionStore.getState();
    expect(state.isStreaming).toBe(false);
    expect(state.messages.at(-1)?.content).toBe("A partial warning");
  });

  // Narration streams before the server commits the turn. A server-side error means the turn was
  // rolled back, so what streamed never happened and must not stay on screen.
  it("discards streamed narration and tool results when the server fails the turn", () => {
    const store = useSessionStore.getState();
    store.addPlayerMessage("I buy the map");
    store.startStreaming();
    store.appendNarrativeChunk("The crone takes your silver.");
    store.addToolResult({ function: "BuyItem", result: "{}" });
    store.discardStreaming();

    const state = useSessionStore.getState();
    expect(state.isStreaming).toBe(false);
    expect(state.streamingText).toBe("");
    expect(state.messages.map((m) => m.role)).toEqual(["user"]);
  });

  // A `reset` marks a new server attempt at the same turn: clear what the first attempt streamed,
  // keep streaming into the same message.
  it("clears the earlier attempt on reset and keeps streaming", () => {
    const store = useSessionStore.getState();
    store.startStreaming();
    store.appendNarrativeChunk("Attempt one.");
    store.addToolResult({ function: "Rest", result: "{}" });
    store.resetStreaming();
    store.appendNarrativeChunk("Attempt two.");
    store.finishStreaming();

    const last = useSessionStore.getState().messages.at(-1);
    expect(last?.content).toBe("Attempt two.");
    expect(last?.toolResults).toEqual([]);
  });

  // A durable turn often arrives as one network chunk, so the reader appends the narrative and
  // finishes the turn in the same batch: `streamingText` is never observably non-empty. Anything
  // waiting for narration (the opening splash) must read the committed message instead.
  it("leaves visible narration when a turn arrives in one batch", () => {
    const store = useSessionStore.getState();
    store.startStreaming();
    store.appendNarrativeChunk("The sky is a slab of ash.");
    store.finishStreaming();

    const state = useSessionStore.getState();
    expect(state.streamingText).toBe("");
    expect(state.messages.some((m) => m.content.length > 0)).toBe(true);
  });
});
