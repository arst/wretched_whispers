import { beforeEach, describe, expect, it } from "vitest";
import { handleMessage, parseSseMessage, readSse } from "./useSseStream";
import { useSessionStore } from "@/stores/sessionStore";

function streamResponse(body: string) {
  const bytes = new TextEncoder().encode(body);
  return new Response(
    new ReadableStream({
      start(controller) {
        controller.enqueue(bytes);
        controller.close();
      },
    }),
  );
}

describe("SSE stream", () => {
  it("parses multiline messages", () => {
    expect(parseSseMessage("event: narrative\ndata: one\ndata: two")).toEqual({
      event: "narrative",
      data: "one\ntwo",
    });
  });

  it("distinguishes a completed turn from an early EOF", async () => {
    const signal = new AbortController().signal;
    const complete = await readSse(
      streamResponse("event: done\ndata: {}\n\n"),
      (message) => (message.event === "done" ? "done" : undefined),
      signal,
    );
    const disconnected = await readSse(
      streamResponse('event: narrative\ndata: {"text":"partial"}\n\n'),
      () => undefined,
      signal,
    );

    expect(complete).toBe("done");
    expect(disconnected).toBe("eof");
  });
});

// Narration streams while the turn is still running on the server, so the event stream can carry
// text that is later voided: by a `reset` (a new attempt started) or by a terminal `error`.
describe("turn event handling", () => {
  beforeEach(() => useSessionStore.getState().reset());

  const narrative = (text: string) => ({
    event: "narrative",
    data: JSON.stringify({ text }),
  });

  it("shows only the latest attempt when the stream contains a reset", () => {
    useSessionStore.getState().startStreaming();

    handleMessage(narrative("Attempt one."));
    handleMessage({ event: "reset", data: "{}" });
    handleMessage(narrative("Attempt two."));
    expect(handleMessage({ event: "done", data: "{}" })).toBe("done");

    expect(useSessionStore.getState().messages.at(-1)?.content).toBe(
      "Attempt two.",
    );
  });

  it("drops streamed narration when the turn ends in an error", () => {
    useSessionStore.getState().startStreaming();

    handleMessage(narrative("The crone takes your silver."));
    expect(
      handleMessage({
        event: "error",
        data: JSON.stringify({ message: "Turn cancelled." }),
      }),
    ).toBe("error");

    const state = useSessionStore.getState();
    expect(state.messages).toEqual([]);
    expect(state.error).toBe("Turn cancelled.");
    expect(state.isStreaming).toBe(false);
  });
});
