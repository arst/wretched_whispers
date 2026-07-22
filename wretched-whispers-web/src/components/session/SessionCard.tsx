"use client";

import Link from "next/link";
import type { SessionPreviewDto } from "@/types/api";

interface SessionCardProps {
  session: SessionPreviewDto;
}

const statusStyles: Record<SessionPreviewDto["status"], string> = {
  "character-creation":
    "border-doom-yellow text-doom-yellow",
  "in-progress":
    "border-green-600 text-green-500",
  ended:
    "border-doom-pink text-doom-pink",
  fallen:
    "border-doom-pink text-doom-pink",
};

const statusLabels: Record<SessionPreviewDto["status"], string> = {
  "character-creation": "Creating Character",
  "in-progress": "In Progress",
  ended: "Ended",
  fallen: "\u2620 Fallen",
};

function formatRelativeTime(dateStr: string): string {
  const now = Date.now();
  const then = new Date(dateStr).getTime();
  const diffMs = now - then;

  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes}m ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;

  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;

  return new Date(dateStr).toLocaleDateString();
}

export default function SessionCard({ session }: SessionCardProps) {
  return (
    <Link
      href={`/sessions/play?id=${session.sessionId}`}
      className={`block bg-doom-card border border-doom-card hover:border-doom-yellow/30 transition-colors p-5 ${
        session.status === "ended" ? "opacity-75" : ""
      }`}
    >
      <div className="flex items-start justify-between gap-3 mb-2">
        <div>
          <h3 className="font-display text-doom-yellow text-lg tracking-wider leading-tight">
            {session.campaignName}
          </h3>
          {session.characterName && (
            <p className="text-doom-bone text-sm mt-0.5">{session.characterName}</p>
          )}
        </div>
        <span
          className={`text-xs uppercase tracking-wider border px-2 py-0.5 shrink-0 ${statusStyles[session.status]}`}
        >
          {statusLabels[session.status]}
        </span>
      </div>

      {session.description && (
        <p className="text-doom-ash text-sm line-clamp-2 mb-3">
          {session.description}
        </p>
      )}

      <div className="flex items-center gap-4 text-xs text-doom-ash">
        {session.currentHp !== null && session.maxHp !== null && (
          <span>
            HP: {session.currentHp}/{session.maxHp}
          </span>
        )}
        <span className="uppercase tracking-wider text-doom-ash/80">
          {session.difficulty}
        </span>
        {session.lastPlayed && (
          <span className="ml-auto">
            {formatRelativeTime(session.lastPlayed)}
          </span>
        )}
      </div>
    </Link>
  );
}
