"use client";

import Link from "next/link";
import { useAuthStore } from "@/stores/authStore";
import { logout } from "@/lib/auth";
import CharacterDrawerToggle from "@/components/character/CharacterDrawerToggle";

export default function Header() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isHydrated = useAuthStore((s) => s.isHydrated);

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
            <Link
              href="/sessions"
              className="text-doom-bone text-sm uppercase tracking-wider hover:text-doom-yellow transition-colors"
            >
              Sessions
            </Link>
            <button
              onClick={logout}
              className="text-doom-ash text-sm uppercase tracking-wider hover:text-doom-pink transition-colors cursor-pointer"
            >
              Logout
            </button>
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
