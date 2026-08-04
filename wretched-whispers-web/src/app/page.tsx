"use client";

import Link from "next/link";
import { useAuthStore } from "@/stores/authStore";

export default function Home() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);

  const beginHref = isAuthenticated ? "/sessions" : "/login";

  return (
    <main className="flex flex-col items-center justify-center min-h-screen px-4">
      <h1 className="font-display text-doom-yellow text-6xl md:text-8xl tracking-wider mb-6 text-center">
        WRETCHED WHISPERS
      </h1>
      <p className="text-doom-ash text-lg md:text-xl text-center max-w-2xl mb-12 italic">
        The world is dying. You are nothing. Play anyway.
      </p>
      <Link
        href={beginHref}
        className="bg-doom-yellow text-doom-black hover:brightness-110 active:brightness-90 px-10 py-4 text-lg font-display uppercase tracking-wider transition-all duration-150"
      >
        BEGIN
      </Link>
      <Link
        href="/login"
        className="mt-6 text-doom-ash text-sm hover:text-doom-yellow transition-colors"
      >
        Already doomed? Sign in
      </Link>
    </main>
  );
}
