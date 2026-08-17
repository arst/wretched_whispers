"use client";

import { useEffect, useState } from "react";
import { useSessionStore } from "@/stores/sessionStore";
import { apiFetch } from "@/lib/api";
import Drawer from "@/components/ui/Drawer";
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
  const mapOpen = useSessionStore((s) => s.activeDrawer === "map");
  const [data, setData] = useState<MapData | null>(null);

  // Fetch on open so places charted during play show up without SSE plumbing
  useEffect(() => {
    if (!mapOpen || !sessionId) return;
    const controller = new AbortController();
    apiFetch(`/sessions/${sessionId}/map`, { signal: controller.signal })
      .then((r) => (r.ok ? r.json() : Promise.reject()))
      .then((d) => {
        if (!controller.signal.aborted) setData(d);
      })
      .catch(() => {
        if (!controller.signal.aborted)
          setData({ pois: [], currentLocationName: null });
      });
    return () => controller.abort();
  }, [mapOpen, sessionId]);

  const pois = data?.pois ?? [];
  const byName = (name: string | null) => pois.find((p) => p.name === name);

  return (
    <Drawer name="map" title="MAP" label="Regional map" width="sm:w-96">
      <div className="px-8 pt-6 pb-8">
        {data === null && <p className="text-doom-ash text-sm">Loading...</p>}
        {data !== null && pois.length === 0 && (
          <p className="text-doom-ash text-sm">Nothing charted yet.</p>
        )}
        {pois.length > 0 && (
          /* ponytail: naive labels, overlap fine below ~15 POIs; nudge/truncate if maps grow denser */
          <svg
            viewBox="-5 -5 110 110"
            className="bg-doom-black aspect-square w-full rounded"
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
    </Drawer>
  );
}
