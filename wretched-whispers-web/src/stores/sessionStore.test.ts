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
});
