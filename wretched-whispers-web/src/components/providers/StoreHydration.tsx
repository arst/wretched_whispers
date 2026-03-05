"use client";

import { useEffect } from "react";
import { useAuthStore } from "@/stores/authStore";

/**
 * Triggers Zustand persist rehydration from localStorage on mount.
 * Required because authStore uses skipHydration: true for SSR safety.
 * Place in the root layout to ensure stores hydrate on every page.
 */
export default function StoreHydration() {
  useEffect(() => {
    useAuthStore.persist.rehydrate();
    useAuthStore.getState().setHydrated();
  }, []);

  return null;
}
