import { useAuthStore } from "@/stores/authStore";

const API_URL = process.env.NEXT_PUBLIC_API_URL!;
const isDesktop = process.env.NEXT_PUBLIC_DESKTOP === "1";
let csrfToken: string | null = null;

export function resetCsrfToken() {
  csrfToken = null;
}

async function getCsrfToken() {
  if (csrfToken) return csrfToken;

  const response = await fetch(`${API_URL}/auth/csrf`, {
    credentials: "include",
  });
  if (response.status === 401) useAuthStore.getState().logout();
  if (!response.ok) throw new Error("Could not establish a secure session");

  const data: { token: string } = await response.json();
  return (csrfToken = data.token);
}

/**
 * Authenticated fetch wrapper for the hosted Identity cookie. Unsafe requests
 * carry ASP.NET's antiforgery token; desktop local auth accepts the same shape.
 */
export async function apiFetch(
  path: string,
  options: RequestInit = {}
): Promise<Response> {
  const method = options.method?.toUpperCase() ?? "GET";
  const headers = new Headers(options.headers);
  if (options.body && !headers.has("Content-Type"))
    headers.set("Content-Type", "application/json");
  if (!isDesktop && !["GET", "HEAD", "OPTIONS"].includes(method))
    headers.set("X-CSRF-TOKEN", await getCsrfToken());

  const response = await fetch(`${API_URL}${path}`, {
    ...options,
    headers,
    credentials: "include",
  });

  if (response.status === 401) useAuthStore.getState().logout();

  return response;
}
