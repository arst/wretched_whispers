"use client";

import Link from "next/link";
import type { SessionPreviewDto } from "@/types/api";
import ClassGlyph from "@/components/session/ClassGlyph";

interface SessionCardProps {
  session: SessionPreviewDto;
}

const statusStyles: Record<SessionPreviewDto["status"], string> = {
  "character-creation": "border-doom-yellow text-doom-yellow",
  "in-progress": "border-green-600 text-green-500",
  ended: "border-doom-pink text-doom-pink",
  fallen: "border-doom-pink text-doom-pink",
};

const statusLabels: Record<SessionPreviewDto["status"], string> = {
  "character-creation": "Creating Character",
  "in-progress": "In Progress",
  ended: "Ended",
  fallen: "\u2620 Fallen",
};

const relative = new Intl.RelativeTimeFormat("en", {
  numeric: "auto",
  style: "narrow",
});
const DAY = 86_400_000;

function formatRelativeTime(dateStr: string): string {
  const elapsed = Date.now() - new Date(dateStr).getTime();
  if (elapsed >= 30 * DAY) return new Date(dateStr).toLocaleDateString();

  const [ms, unit] =
    elapsed >= DAY
      ? ([DAY, "day"] as const)
      : elapsed >= 3_600_000
        ? ([3_600_000, "hour"] as const)
        : ([60_000, "minute"] as const);
  return relative.format(-Math.floor(elapsed / ms), unit);
}

export default function SessionCard({ session }: SessionCardProps) {
  return (
    <Link
      href={`/sessions/play?id=${session.sessionId}`}
      className={`bg-doom-card border-doom-card hover:border-doom-yellow/30 block border p-5 transition-colors ${
        session.status === "ended" ? "opacity-75" : ""
      }`}
    >
      <div className="mb-2 flex items-start justify-between gap-3">
        <div>
          <h3 className="font-display text-doom-yellow text-lg leading-tight tracking-wider">
            {session.campaignName}
          </h3>
          {session.characterName && (
            <p className="text-doom-bone mt-0.5 flex items-center gap-1.5 text-sm">
              {session.characterClass && (
                <ClassGlyph
                  characterClass={session.characterClass}
                  className="text-doom-ash h-4 w-4 shrink-0"
                />
              )}
              {session.characterClass
                ? `${session.characterName}, the ${session.characterClass}`
                : session.characterName}
            </p>
          )}
        </div>
        <span
          className={`shrink-0 border px-2 py-0.5 text-xs tracking-wider uppercase ${statusStyles[session.status]}`}
        >
          {statusLabels[session.status]}
        </span>
      </div>

      {session.description && (
        <p className="text-doom-ash mb-3 line-clamp-2 text-sm">
          {session.description}
        </p>
      )}

      <div className="text-doom-ash flex items-center gap-4 text-xs">
        {session.currentHp !== null && session.maxHp !== null && (
          <span>
            HP: {session.currentHp}/{session.maxHp}
          </span>
        )}
        <span className="text-doom-ash/80 tracking-wider uppercase">
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
