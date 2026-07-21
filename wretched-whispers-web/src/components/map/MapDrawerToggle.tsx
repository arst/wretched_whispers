"use client";

import { useSessionStore } from "@/stores/sessionStore";

export default function MapDrawerToggle() {
  const toggleMap = useSessionStore((s) => s.toggleMap);
  const status = useSessionStore((s) => s.status);
  const mapOpen = useSessionStore((s) => s.mapOpen);
  const currentLocationName = useSessionStore((s) => s.currentLocationName);

  const isVisible = status === "in-progress" || status === "ended";

  return (
    <div
      className={`transition-opacity duration-200 ${
        isVisible ? "opacity-100" : "opacity-0 pointer-events-none"
      }`}
    >
      <button
        onClick={toggleMap}
        className="min-h-[44px] flex items-center cursor-pointer"
        aria-label={mapOpen ? "Close map" : "Open map"}
      >
        <span className="flex items-center gap-1.5 rounded border border-doom-yellow/30 bg-doom-yellow/10 px-2 py-1 text-xs font-bold uppercase text-doom-yellow hover:border-doom-yellow/60 transition-colors">
          <span>Map</span>
          {currentLocationName && (
            <span className="text-doom-bone">{currentLocationName}</span>
          )}
        </span>
      </button>
    </div>
  );
}
