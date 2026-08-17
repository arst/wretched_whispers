import { useAuthStore } from "@/stores/authStore";
import { isStandalone } from "@/lib/deployment";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";
let csrfToken: string | null = null;

export function resetCsrfToken() {
  csrfToken = null;
}

async function getCsrfToken() {
  if (csrfToken) return csrfToken;

  const response = await fetch(`${API_URL}/api/auth/csrf`, {
    credentials: "include",
  });
  if (response.status === 401) useAuthStore.getState().logout();
  if (!response.ok) throw new Error("Could not establish a secure session");

  const data: { token: string } = await response.json();
  return (csrfToken = data.token);
}

/**
 * Authenticated fetch wrapper for the hosted Identity cookie. Unsafe requests
 * carry ASP.NET's antiforgery token; standalone local auth does not need one.
 */
export async function apiFetch(
  path: string,
  options: RequestInit = {},
): Promise<Response> {
  const method = options.method?.toUpperCase() ?? "GET";
  const headers = new Headers(options.headers);
  if (options.body && !headers.has("Content-Type"))
    headers.set("Content-Type", "application/json");
  if (!isStandalone && !["GET", "HEAD", "OPTIONS"].includes(method))
    headers.set("X-CSRF-TOKEN", await getCsrfToken());

  const response = await fetch(`${API_URL}/api${path}`, {
    ...options,
    headers,
    credentials: "include",
  });

  if (response.status === 401) useAuthStore.getState().logout();

  return response;
}

/**
 * Pulls the human-readable message out of a failed response. The API answers every error in RFC
 * 9457 ProblemDetails — the same shape ASP.NET Identity's own endpoints use — so the text lives in
 * `detail`, with `title` as the fallback for framework-generated problems that carry no detail.
 */
export async function apiErrorMessage(
  response: Response,
  fallback: string,
): Promise<string> {
  const problem = await response.json().catch(() => null);
  return (
    problem?.detail ?? problem?.title ?? `${fallback} (${response.status})`
  );
}
