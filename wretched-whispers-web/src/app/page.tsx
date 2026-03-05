"use client";

import Link from "next/link";
import { useAuthStore } from "@/stores/authStore";
import Button from "@/components/ui/Button";

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
      <Link href={beginHref}>
        <Button variant="primary" className="text-lg px-10 py-4">
          BEGIN
        </Button>
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
