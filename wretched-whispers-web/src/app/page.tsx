"use client";

import Link from "next/link";
import { useAuthStore } from "@/stores/authStore";

export default function Home() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);

  const beginHref = isAuthenticated ? "/sessions" : "/login";

  return (
    <main className="flex min-h-screen flex-col items-center justify-center px-4">
      <h1 className="font-display text-doom-yellow mb-6 text-center text-6xl tracking-wider md:text-8xl">
        WRETCHED WHISPERS
      </h1>
      <p className="text-doom-ash mb-12 max-w-2xl text-center text-lg italic md:text-xl">
        The world is dying. You are nothing. Play anyway.
      </p>
      <Link
        href={beginHref}
        className="bg-doom-yellow text-doom-black font-display px-10 py-4 text-lg tracking-wider uppercase transition-all duration-150 hover:brightness-110 active:brightness-90"
      >
        BEGIN
      </Link>
      <Link
        href="/login"
        className="text-doom-ash hover:text-doom-yellow mt-6 text-sm transition-colors"
      >
        Already doomed? Sign in
      </Link>
    </main>
  );
}
