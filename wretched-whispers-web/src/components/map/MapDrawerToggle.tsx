"use client";

import { useSessionStore } from "@/stores/sessionStore";
import DrawerToggle from "@/components/ui/DrawerToggle";

export default function MapDrawerToggle() {
  const status = useSessionStore((s) => s.status);
  const currentLocationName = useSessionStore((s) => s.currentLocationName);

  return (
    <DrawerToggle
      name="map"
      label="Map"
      visible={status === "in-progress" || status === "ended"}
      badge={currentLocationName || undefined}
    />
  );
}
