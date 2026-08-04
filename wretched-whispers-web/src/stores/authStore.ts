import { create } from "zustand";

interface AuthState {
  isAuthenticated: boolean;
  isHydrated: boolean;
  setAuthenticated: (authenticated: boolean) => void;
  logout: () => void;
  setHydrated: () => void;
}

// Desktop build has no login — the backend authenticates every request as the local user — so the
// client starts already "authenticated" and the login gate (AuthGuard) is bypassed.
const isDesktop = process.env.NEXT_PUBLIC_DESKTOP === "1";

export const useAuthStore = create<AuthState>()((set) => ({
  isAuthenticated: isDesktop,
  isHydrated: false,
  setAuthenticated: (isAuthenticated) => set({ isAuthenticated }),
  logout: () => set({ isAuthenticated: false }),
  setHydrated: () => set({ isHydrated: true }),
}));
