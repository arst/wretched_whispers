"use client";

import Link from "next/link";
import { useAuthStore } from "@/stores/authStore";
import { useDesktopSettingsStore } from "@/stores/desktopSettingsStore";
import { logout } from "@/lib/auth";
import CharacterDrawerToggle from "@/components/character/CharacterDrawerToggle";
import JournalDrawerToggle from "@/components/journal/JournalDrawerToggle";
import MapDrawerToggle from "@/components/map/MapDrawerToggle";
import { isStandalone } from "@/lib/deployment";

export default function Header() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isHydrated = useAuthStore((s) => s.isHydrated);
  const openSettings = useDesktopSettingsStore((s) => s.setOpen);

  return (
    <header className="bg-doom-dark border-doom-card fixed top-0 right-0 left-0 z-40 flex h-14 items-center border-b px-4">
      <Link
        href="/"
        className="font-display text-doom-yellow text-lg tracking-wider transition-all hover:brightness-110"
      >
        WRETCHED WHISPERS
      </Link>

      <nav className="ml-auto flex items-center gap-4">
        {!isHydrated ? (
          /* Skeleton placeholder while store hydrates */
          <div className="bg-doom-card h-4 w-20 animate-pulse rounded" />
        ) : isAuthenticated ? (
          <>
            <CharacterDrawerToggle />
            <JournalDrawerToggle />
            <MapDrawerToggle />
            <Link
              href="/sessions"
              className="text-doom-bone hover:text-doom-yellow text-sm tracking-wider uppercase transition-colors"
            >
              Sessions
            </Link>
            {isStandalone ? (
              <button
                onClick={() => openSettings(true)}
                aria-label="Settings"
                title="Settings"
                className="text-doom-ash hover:text-doom-yellow cursor-pointer text-lg leading-none transition-colors"
              >
                ⚙
              </button>
            ) : (
              <button
                onClick={() => void logout()}
                className="text-doom-ash hover:text-doom-pink cursor-pointer text-sm tracking-wider uppercase transition-colors"
              >
                Logout
              </button>
            )}
          </>
        ) : (
          <Link
            href="/login"
            className="text-doom-bone hover:text-doom-yellow text-sm tracking-wider uppercase transition-colors"
          >
            Login
          </Link>
        )}
      </nav>
    </header>
  );
}
