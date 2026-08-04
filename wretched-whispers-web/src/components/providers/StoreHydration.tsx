"use client";

import { useEffect } from "react";
import { useAuthStore } from "@/stores/authStore";
import { verifyToken } from "@/lib/auth";

/**
 * Resolves the hosted Identity cookie before guarded pages render. Desktop
 * starts authenticated through its fixed local-user backend.
 */
export default function StoreHydration() {
  useEffect(() => {
    const store = useAuthStore.getState();
    if (store.isAuthenticated) {
      store.setHydrated();
      return;
    }

    void verifyToken().then((authenticated) => {
      const current = useAuthStore.getState();
      current.setAuthenticated(authenticated);
      current.setHydrated();
    });
  }, []);

  return null;
}
