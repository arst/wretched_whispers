import { useAuthStore } from "@/stores/authStore";

const API_URL = process.env.NEXT_PUBLIC_API_URL!;

/**
 * Authenticated fetch wrapper that attaches Bearer token from the auth store
 * and handles 401 responses by attempting token refresh.
 *
 * On successful refresh: updates store, retries original request once.
 * On failed refresh: calls logout(), returns the 401 response (no retry).
 */
export async function apiFetch(
  path: string,
  options: RequestInit = {}
): Promise<Response> {
  const { accessToken, refreshToken, setTokens, logout } =
    useAuthStore.getState();

  const headers: HeadersInit = {
    "Content-Type": "application/json",
    ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    ...(options.headers as Record<string, string>),
  };

  const response = await fetch(`${API_URL}${path}`, {
    ...options,
    headers,
  });

  if (response.status === 401 && refreshToken) {
    const refreshResponse = await fetch(`${API_URL}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });

    if (refreshResponse.ok) {
      const data = await refreshResponse.json();
      setTokens(data.accessToken, data.refreshToken, data.expiresIn);

      // Retry original request with new token
      return fetch(`${API_URL}${path}`, {
        ...options,
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${data.accessToken}`,
          ...(options.headers as Record<string, string>),
        },
      });
    } else {
      logout();
    }
  }

  return response;
}
