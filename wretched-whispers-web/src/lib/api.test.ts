import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiFetch, resetCsrfToken } from "./api";
import { useAuthStore } from "@/stores/authStore";

describe("apiFetch", () => {
  beforeEach(() => {
    resetCsrfToken();
    useAuthStore.getState().setAuthenticated(true);
    vi.restoreAllMocks();
  });

  it("adds cookie credentials and antiforgery to unsafe requests", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ token: "csrf" })))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await apiFetch("/sessions", { method: "POST", body: "{}" });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/auth/csrf");
    const [, request] = fetchMock.mock.calls[1];
    expect(fetchMock.mock.calls[1][0]).toBe("/api/sessions");
    expect(request.credentials).toBe("include");
    expect(new Headers(request.headers).get("X-CSRF-TOKEN")).toBe("csrf");
  });

  it("clears authentication after a 401", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 401 })));

    await apiFetch("/sessions");

    expect(useAuthStore.getState().isAuthenticated).toBe(false);
  });
});
