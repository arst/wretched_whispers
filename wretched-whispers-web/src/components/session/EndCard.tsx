"use client";

import { useState, useEffect, useRef } from "react";
import { useRouter } from "next/navigation";
import { apiFetch } from "@/lib/api";
import type { CreateSessionResponse } from "@/types/api";

interface EndCardProps {
  characterName: string;
  isDead: boolean;
  worldEnded: boolean;
  miseryCount: number;
  currentDay: number;
  onRestart: () => void;
}

export default function EndCard({
  characterName,
  isDead,
  worldEnded,
  miseryCount,
  currentDay,
  onRestart,
}: EndCardProps) {
  const router = useRouter();
  const [restarting, setRestarting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [visible, setVisible] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);

  // Trigger fade-in after mount
  useEffect(() => {
    setVisible(true);
  }, []);

  // Focus the button after animation completes
  useEffect(() => {
    if (visible) {
      const timer = setTimeout(() => {
        buttonRef.current?.focus();
      }, 700);
      return () => clearTimeout(timer);
    }
  }, [visible]);

  const isApocalypse = worldEnded;
  const title = isApocalypse
    ? "THE WORLD HAS ENDED"
    : "YOUR WRETCH HAS FALLEN";
  const accentClass = isApocalypse ? "text-[#ffe000]" : "text-[#ff1493]";
  const borderClass = isApocalypse ? "border-[#ffe000]" : "border-[#ff1493]";
  const buttonBg = isApocalypse ? "bg-[#ffe000]" : "bg-[#ff1493]";

  async function handleRestart() {
    setRestarting(true);
    setError(null);
    try {
      const res = await apiFetch("/sessions", { method: "POST" });
      if (!res.ok) throw new Error("Failed");
      const data: CreateSessionResponse = await res.json();
      onRestart();
      router.push(`/sessions/${data.sessionId}`);
    } catch {
      setError("The fates resist. Try again.");
      setRestarting(false);
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Game over"
      className="fixed inset-0 z-50 flex items-center justify-center"
    >
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-[#0a0a0a]/80 transition-opacity duration-[400ms]"
        style={{ opacity: visible ? 1 : 0 }}
      />

      {/* Panel */}
      <div
        className={`relative max-w-sm w-full mx-4 bg-[#141414] border ${borderClass} p-8 transition-all duration-[600ms] delay-200`}
        style={{
          opacity: visible ? 1 : 0,
          transform: visible ? "translateY(0)" : "translateY(16px)",
        }}
      >
        {/* Title */}
        <h2
          className={`font-display text-2xl font-bold uppercase tracking-widest ${accentClass} text-center`}
        >
          {title}
        </h2>

        {/* Character name */}
        <p className="font-display text-lg text-[#e8e0d4] text-center mt-4">
          {characterName}
        </p>

        {/* Stats row */}
        <div className="flex justify-center gap-4 mt-4 text-xs text-[#8a8a8a] uppercase tracking-wider">
          <span>Day {currentDay}</span>
          <span>{miseryCount} Miseries Witnessed</span>
        </div>

        {/* Divider */}
        <div className="border-t border-[#1a1a1a] my-4" />

        {/* Begin Anew button */}
        <button
          ref={buttonRef}
          onClick={handleRestart}
          disabled={restarting}
          className={`w-full min-h-[44px] font-display text-sm font-bold uppercase tracking-widest ${buttonBg} text-[#0a0a0a] rounded cursor-pointer hover:brightness-110 transition-all disabled:cursor-not-allowed ${
            restarting ? "animate-pulse" : ""
          }`}
        >
          {restarting ? "SUMMONING..." : "BEGIN ANEW"}
        </button>

        {/* Error text */}
        {error && (
          <p className="text-[#ff1493] text-xs text-center mt-2">{error}</p>
        )}
      </div>
    </div>
  );
}
