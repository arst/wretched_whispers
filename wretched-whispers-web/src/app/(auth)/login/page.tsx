"use client";

import { useState, useEffect, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuthStore } from "@/stores/authStore";
import { login } from "@/lib/auth";
import Button from "@/components/ui/Button";
import Input from "@/components/ui/Input";

export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isHydrated = useAuthStore((s) => s.isHydrated);
  const router = useRouter();

  useEffect(() => {
    if (isHydrated && isAuthenticated) {
      router.replace("/sessions");
    }
  }, [isHydrated, isAuthenticated, router]);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError("");
    setLoading(true);

    try {
      await login(email, password);
      router.push("/sessions");
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "The darkness rejects you. Check your credentials."
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-6">
      <h1 className="font-display text-doom-yellow text-3xl tracking-wider text-center">
        SIGN IN, WRETCH
      </h1>

      <Input
        label="Email"
        type="email"
        placeholder="your@soul.lost"
        value={email}
        onChange={(e) => setEmail(e.target.value)}
        required
        autoComplete="email"
      />

      <Input
        label="Password"
        type="password"
        placeholder="whisper it..."
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        required
        autoComplete="current-password"
      />

      {error && (
        <p className="text-doom-pink text-sm text-center">{error}</p>
      )}

      <Button type="submit" variant="primary" loading={loading}>
        Enter the Darkness
      </Button>

      <p className="text-doom-ash text-sm text-center">
        No soul yet?{" "}
        <Link
          href="/register"
          className="text-doom-yellow hover:underline"
        >
          Create one
        </Link>
      </p>
    </form>
  );
}
