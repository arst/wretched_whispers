import { describe, expect, it } from "vitest";
import { parseSseMessage, readSse } from "./useSseStream";

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
