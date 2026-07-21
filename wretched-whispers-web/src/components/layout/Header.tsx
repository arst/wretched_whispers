"use client";

import Link from "next/link";
import { useAuthStore } from "@/stores/authStore";
import { useDesktopSettingsStore } from "@/stores/desktopSettingsStore";
import { logout } from "@/lib/auth";
import CharacterDrawerToggle from "@/components/character/CharacterDrawerToggle";
import JournalDrawerToggle from "@/components/journal/JournalDrawerToggle";
import MapDrawerToggle from "@/components/map/MapDrawerToggle";

const isDesktop = process.env.NEXT_PUBLIC_DESKTOP === "1";

export default function Header() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isHydrated = useAuthStore((s) => s.isHydrated);
  const openSettings = useDesktopSettingsStore((s) => s.setOpen);

  return (
    <header className="fixed top-0 left-0 right-0 z-40 h-14 bg-doom-dark border-b border-doom-card flex items-center px-4">
      <Link
        href="/"
        className="font-display text-doom-yellow text-lg tracking-wider hover:brightness-110 transition-all"
      >
        WRETCHED WHISPERS
      </Link>

      <nav className="ml-auto flex items-center gap-4">
        {!isHydrated ? (
          /* Skeleton placeholder while store hydrates */
          <div className="w-20 h-4 bg-doom-card animate-pulse rounded" />
        ) : isAuthenticated ? (
          <>
            <CharacterDrawerToggle />
            <JournalDrawerToggle />
            <MapDrawerToggle />
            <Link
              href="/sessions"
              className="text-doom-bone text-sm uppercase tracking-wider hover:text-doom-yellow transition-colors"
            >
              Sessions
            </Link>
            {isDesktop ? (
              <button
                onClick={() => openSettings(true)}
                aria-label="Settings"
                title="Settings"
                className="text-doom-ash text-lg hover:text-doom-yellow transition-colors cursor-pointer leading-none"
              >
                ⚙
              </button>
            ) : (
              <button
                onClick={logout}
                className="text-doom-ash text-sm uppercase tracking-wider hover:text-doom-pink transition-colors cursor-pointer"
              >
                Logout
              </button>
            )}
          </>
        ) : (
          <Link
            href="/login"
            className="text-doom-bone text-sm uppercase tracking-wider hover:text-doom-yellow transition-colors"
          >
            Login
          </Link>
        )}
      </nav>
    </header>
  );
}
