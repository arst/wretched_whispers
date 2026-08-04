import { create } from "zustand";
import { isStandalone } from "@/lib/deployment";

interface AuthState {
  isAuthenticated: boolean;
  isHydrated: boolean;
  setAuthenticated: (authenticated: boolean) => void;
  logout: () => void;
  setHydrated: () => void;
}

// Standalone builds have no login — the backend authenticates every request as the local user — so the
// client starts already "authenticated" and the login gate (AuthGuard) is bypassed.
export const useAuthStore = create<AuthState>()((set) => ({
  isAuthenticated: isStandalone,
  isHydrated: false,
  setAuthenticated: (isAuthenticated) => set({ isAuthenticated }),
  logout: () => set({ isAuthenticated: false }),
  setHydrated: () => set({ isHydrated: true }),
}));
