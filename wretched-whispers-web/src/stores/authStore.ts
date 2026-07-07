import { create } from "zustand";
import { persist } from "zustand/middleware";

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  expiresAt: number | null;
  isAuthenticated: boolean;
  isHydrated: boolean;
  setTokens: (access: string, refresh: string, expiresIn: number) => void;
  logout: () => void;
  setHydrated: () => void;
}

// Desktop build has no login — the backend authenticates every request as the local user — so the
// client starts already "authenticated" and the login gate (AuthGuard) is bypassed.
const isDesktop = process.env.NEXT_PUBLIC_DESKTOP === "1";

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      expiresAt: null,
      isAuthenticated: isDesktop,
      isHydrated: false,

      setTokens: (access, refresh, expiresIn) =>
        set({
          accessToken: access,
          refreshToken: refresh,
          expiresAt: Date.now() + expiresIn * 1000,
          isAuthenticated: true,
        }),

      logout: () =>
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          isAuthenticated: false,
        }),

      setHydrated: () => set({ isHydrated: true }),
    }),
    {
      name: "ww-auth",
      skipHydration: true,
      partialize: (state) => ({
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        expiresAt: state.expiresAt,
        isAuthenticated: state.isAuthenticated,
      }),
    }
  )
);
