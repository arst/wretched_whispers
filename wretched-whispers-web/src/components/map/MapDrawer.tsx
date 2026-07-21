"use client";

import { useEffect, useRef, useState } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import { apiFetch } from "@/lib/api";
import type { PoiDto } from "@/types/api";

const POI_COLORS: Record<string, string> = {
  Town: "var(--color-doom-yellow)",
  Dungeon: "var(--color-doom-pink)",
  Landmark: "var(--color-doom-bone)",
  Ruin: "var(--color-doom-bone)",
  Camp: "var(--color-doom-ash)",
};

interface MapData {
  pois: PoiDto[];
  currentLocationName: string | null;
}

export default function MapDrawer() {
  const sessionId = useSessionStore((s) => s.sessionId);
  const mapOpen = useSessionStore((s) => s.mapOpen);
  const toggleMap = useSessionStore((s) => s.toggleMap);
  const drawerRef = useRef<HTMLDivElement>(null);
  const [mounted, setMounted] = useState(false);
  const [data, setData] = useState<MapData | null>(null);

  // Mount/unmount with transition support
  useEffect(() => {
    if (mapOpen) {
      setMounted(true);
    } else {
      const timer = setTimeout(() => setMounted(false), 200);
      return () => clearTimeout(timer);
    }
  }, [mapOpen]);

  // Fetch on open so places charted during play show up without SSE plumbing
  useEffect(() => {
    if (!mapOpen || !sessionId) return;
    setData(null);
    apiFetch(`/sessions/${sessionId}/map`)
      .then((r) => (r.ok ? r.json() : Promise.reject()))
      .then((d) => setData(d))
      .catch(() => setData({ pois: [], currentLocationName: null }));
  }, [mapOpen, sessionId]);

  // Focus trap
  useEffect(() => {
    if (!mapOpen || !drawerRef.current) return;

    const previousFocus = document.activeElement as HTMLElement | null;
    const drawer = drawerRef.current;

    const focusableSelector =
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';

    function getFocusableElements() {
      return Array.from(
        drawer.querySelectorAll<HTMLElement>(focusableSelector)
      );
    }

    // Focus first element after transition
    const focusTimer = setTimeout(() => {
      const elements = getFocusableElements();
      if (elements.length > 0) elements[0].focus();
    }, 50);

    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        toggleMap();
        return;
      }

      if (e.key === "Tab") {
        const elements = getFocusableElements();
        if (elements.length === 0) return;

        const first = elements[0];
        const last = elements[elements.length - 1];

        if (e.shiftKey) {
          if (document.activeElement === first) {
            e.preventDefault();
            last.focus();
          }
        } else {
          if (document.activeElement === last) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    }

    document.addEventListener("keydown", handleKeyDown);

    return () => {
      clearTimeout(focusTimer);
      document.removeEventListener("keydown", handleKeyDown);
      if (previousFocus && typeof previousFocus.focus === "function") {
        previousFocus.focus();
      }
    };
  }, [mapOpen, toggleMap]);

  if (!mounted) return null;

  const pois = data?.pois ?? [];
  const byName = (name: string | null) =>
    pois.find((p) => p.name === name);

  return (
    <>
      {/* Backdrop */}
      <div
        className={`fixed inset-0 z-50 bg-[#0a0a0a]/60 transition-opacity duration-200 ${
          mapOpen ? "opacity-100" : "opacity-0"
        }`}
        onClick={toggleMap}
        aria-hidden="true"
      />

      {/* Drawer panel */}
      <div
        ref={drawerRef}
        role="dialog"
        aria-modal="true"
        aria-label="Regional map"
        className={`fixed top-0 right-0 z-50 h-full w-full sm:w-96 bg-doom-dark transform transition-transform duration-200 ease-out overflow-y-auto ${
          mapOpen ? "translate-x-0" : "translate-x-full"
        }`}
      >
        {/* Header row */}
        <div className="px-8 pt-8 pb-0 flex items-center justify-between">
          <h2 className="font-display text-lg font-bold text-doom-yellow">
            MAP
          </h2>
          <button
            onClick={toggleMap}
            aria-label="Close map"
            className="text-doom-ash hover:text-doom-bone text-xl cursor-pointer"
          >
            {"×"}
          </button>
        </div>

        <div className="px-8 pt-6 pb-8">
          {data === null && (
            <p className="text-doom-ash text-sm">Loading...</p>
          )}
          {data !== null && pois.length === 0 && (
            <p className="text-doom-ash text-sm">Nothing charted yet.</p>
          )}
          {pois.length > 0 && (
            /* ponytail: naive labels, overlap fine below ~15 POIs; nudge/truncate if maps grow denser */
            <svg
              viewBox="-5 -5 110 110"
              className="w-full aspect-square bg-doom-black rounded"
              role="img"
              aria-label="Map of charted places"
            >
              {pois
                .filter((p) => p.connectedTo)
                .map((p) => {
                  const target = byName(p.connectedTo);
                  if (!target) return null;
                  return (
                    <line
                      key={`${p.name}-${p.connectedTo}`}
                      x1={p.x}
                      y1={p.y}
                      x2={target.x}
                      y2={target.y}
                      stroke="var(--color-doom-ash)"
                      strokeWidth="0.4"
                      strokeDasharray="2 2"
                    />
                  );
                })}
              {pois.map((p) => (
                <g key={p.name}>
                  {p.name === data?.currentLocationName && (
                    <circle
                      cx={p.x}
                      cy={p.y}
                      r="3.5"
                      fill="none"
                      stroke="var(--color-doom-yellow)"
                      strokeWidth="0.5"
                    />
                  )}
                  <circle
                    cx={p.x}
                    cy={p.y}
                    r="1.5"
                    fill={POI_COLORS[p.type] ?? "var(--color-doom-bone)"}
                  />
                  <text
                    x={p.x}
                    y={p.y + 4.5}
                    textAnchor="middle"
                    fontSize="3"
                    fill="var(--color-doom-bone)"
                  >
                    {p.name}
                  </text>
                </g>
              ))}
            </svg>
          )}
        </div>
      </div>
    </>
  );
}
