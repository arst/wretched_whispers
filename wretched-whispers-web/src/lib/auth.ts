import { useAuthStore } from "@/stores/authStore";
import type { LoginResponse } from "@/types/api";

const API_URL = process.env.NEXT_PUBLIC_API_URL!;

/**
 * Log in with email and password. Updates the auth store on success.
 * Uses ?useCookies=false to get Bearer token response from ASP.NET Identity.
 */
export async function login(
  email: string,
  password: string
): Promise<LoginResponse> {
  const response = await fetch(`${API_URL}/auth/login?useCookies=false`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });

  if (!response.ok) {
    throw new Error(
      response.status === 401
        ? "Invalid email or password"
        : `Login failed (${response.status})`
    );
  }

  const data: LoginResponse = await response.json();
  useAuthStore
    .getState()
    .setTokens(data.accessToken, data.refreshToken, data.expiresIn);

  return data;
}

/**
 * Register a new account with email and password.
 * Does not automatically log in -- caller should call login() after.
 */
export async function register(
  email: string,
  password: string
): Promise<void> {
  const response = await fetch(`${API_URL}/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
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
  const { accessToken } = useAuthStore.getState();
  if (!accessToken) return false;

  try {
    const response = await fetch(`${API_URL}/auth/me`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    return response.ok;
  } catch {
    return false;
  }
}

/**
 * Clear all auth state. Immediate -- no API call needed.
 */
export function logout(): void {
  useAuthStore.getState().logout();
}
