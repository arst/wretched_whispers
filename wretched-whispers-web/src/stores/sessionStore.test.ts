import { beforeEach, describe, expect, it } from "vitest";
import { useSessionStore } from "./sessionStore";

describe("session store", () => {
  beforeEach(() => useSessionStore.getState().reset());

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
