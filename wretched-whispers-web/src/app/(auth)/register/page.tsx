"use client";

import { useState, useEffect, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuthStore } from "@/stores/authStore";
import { register, login } from "@/lib/auth";
import Button from "@/components/ui/Button";
import Input from "@/components/ui/Input";

export default function RegisterPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
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

  function validate(): string | null {
    if (password.length < 8) {
      return "Password must be at least 8 characters. Even wretches have standards.";
    }
    if (password !== confirmPassword) {
      return "Passwords do not match. The void is unforgiving.";
    }
    return null;
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError("");

    const validationError = validate();
    if (validationError) {
      setError(validationError);
      return;
    }

    setLoading(true);

    try {
      await register(email, password);
      await login(email, password);
      router.push("/sessions");
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "The abyss spits you back. Try again.",
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-6">
      <h1 className="font-display text-doom-yellow text-center text-3xl tracking-wider">
        FORGE YOUR SOUL
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
        placeholder="at least 8 characters..."
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        required
        autoComplete="new-password"
      />

      <Input
        label="Confirm Password"
        type="password"
        placeholder="repeat your doom..."
        value={confirmPassword}
        onChange={(e) => setConfirmPassword(e.target.value)}
        required
        autoComplete="new-password"
      />

      {error && <p className="text-doom-pink text-center text-sm">{error}</p>}

      <Button type="submit" variant="primary" loading={loading}>
        Enter the Abyss
      </Button>

      <p className="text-doom-ash text-center text-sm">
        Already damned?{" "}
        <Link href="/login" className="text-doom-yellow hover:underline">
          Sign in
        </Link>
      </p>
    </form>
  );
}
