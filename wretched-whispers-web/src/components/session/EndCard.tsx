"use client";

import { useEffect, useRef } from "react";
import { useRouter } from "next/navigation";

interface EndCardProps {
  characterName: string;
  worldEnded: boolean;
  miseryCount: number;
  currentDay: number;
}

export default function EndCard({
  characterName,
  worldEnded,
  miseryCount,
  currentDay,
}: EndCardProps) {
  const router = useRouter();
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const element = dialog.current;
    element?.showModal();
    return () => element?.close();
  }, []);

  const isApocalypse = worldEnded;
  const title = isApocalypse
    ? "THE WORLD HAS ENDED"
    : "YOUR WRETCH HAS FALLEN";
  const accentClass = isApocalypse ? "text-[#ffe000]" : "text-[#ff1493]";
  const borderClass = isApocalypse ? "border-[#ffe000]" : "border-[#ff1493]";
  const buttonBg = isApocalypse ? "bg-[#ffe000]" : "bg-[#ff1493]";

  return (
    <dialog
      ref={dialog}
      aria-label="Game over"
      onCancel={(event) => event.preventDefault()}
      className="m-auto max-w-none bg-transparent p-0 text-doom-bone backdrop:bg-[#0a0a0a]/80"
    >
      {/* Panel */}
      <div
        className={`max-w-sm w-[calc(100vw_-_2rem)] bg-[#141414] border ${borderClass} p-8`}
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
          autoFocus
          onClick={() => router.push("/sessions")}
          className={`w-full min-h-[44px] font-display text-sm font-bold uppercase tracking-widest ${buttonBg} text-[#0a0a0a] rounded cursor-pointer hover:brightness-110 transition-all`}
        >
          BEGIN ANEW
        </button>
      </div>
    </dialog>
  );
}
