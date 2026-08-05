import { useAuthStore } from "@/stores/authStore";
import { apiFetch, resetCsrfToken } from "@/lib/api";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

/**
 * Log in with email and password. Updates the auth store on success.
 * Cookie auth (?useCookies=true), so writes must carry an antiforgery token —
 * see apiFetch. Bearer tokens can read but not mutate.
 */
export async function login(
  email: string,
  password: string
): Promise<void> {
  const response = await fetch(`${API_URL}/api/auth/login?useCookies=true`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ email, password }),
  });

  if (!response.ok) {
    throw new Error(
      response.status === 401
        ? "Invalid email or password"
        : `Login failed (${response.status})`
    );
  }

  resetCsrfToken();
  useAuthStore.getState().setAuthenticated(true);
}

/**
 * Register a new account with email and password.
 * Does not automatically log in -- caller should call login() after.
 */
export async function register(
  email: string,
  password: string
): Promise<void> {
  const response = await fetch(`${API_URL}/api/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ email, password }),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `Registration failed (${response.status})`);
  }
}

/**
 * Verify the current access token is still valid by calling GET /auth/me.
 * Returns true if the token is accepted, false otherwise.
 */
export async function verifyToken(): Promise<boolean> {
  try {
    const response = await fetch(`${API_URL}/api/auth/me`, {
      credentials: "include",
    });
    return response.ok;
  } catch {
    return false;
  }
}

/**
 * Clear all auth state. Immediate -- no API call needed.
 */
export async function logout(): Promise<void> {
  try {
    await apiFetch("/auth/logout", { method: "POST" });
  } finally {
    resetCsrfToken();
    useAuthStore.getState().logout();
  }
}
