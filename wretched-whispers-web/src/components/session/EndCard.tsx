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
  const title = isApocalypse ? "THE WORLD HAS ENDED" : "YOUR WRETCH HAS FALLEN";
  const accentClass = isApocalypse ? "text-[#ffe000]" : "text-[#ff1493]";
  const borderClass = isApocalypse ? "border-[#ffe000]" : "border-[#ff1493]";
  const buttonBg = isApocalypse ? "bg-[#ffe000]" : "bg-[#ff1493]";

  return (
    <dialog
      ref={dialog}
      aria-label="Game over"
      onCancel={(event) => event.preventDefault()}
      className="text-doom-bone m-auto max-w-none bg-transparent p-0 backdrop:bg-[#0a0a0a]/80"
    >
      {/* Panel */}
      <div
        className={`w-[calc(100vw_-_2rem)] max-w-sm border bg-[#141414] ${borderClass} p-8`}
      >
        {/* Title */}
        <h2
          className={`font-display text-2xl font-bold tracking-widest uppercase ${accentClass} text-center`}
        >
          {title}
        </h2>

        {/* Character name */}
        <p className="font-display mt-4 text-center text-lg text-[#e8e0d4]">
          {characterName}
        </p>

        {/* Stats row */}
        <div className="mt-4 flex justify-center gap-4 text-xs tracking-wider text-[#8a8a8a] uppercase">
          <span>Day {currentDay}</span>
          <span>{miseryCount} Miseries Witnessed</span>
        </div>

        {/* Divider */}
        <div className="my-4 border-t border-[#1a1a1a]" />

        {/* Begin Anew button */}
        <button
          autoFocus
          onClick={() => router.push("/sessions")}
          className={`font-display min-h-[44px] w-full text-sm font-bold tracking-widest uppercase ${buttonBg} cursor-pointer rounded text-[#0a0a0a] transition-all hover:brightness-110`}
        >
          BEGIN ANEW
        </button>
      </div>
    </dialog>
  );
}
