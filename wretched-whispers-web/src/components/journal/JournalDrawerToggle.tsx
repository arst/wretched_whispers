"use client";

import { useSessionStore } from "@/stores/sessionStore";
import DrawerToggle from "@/components/ui/DrawerToggle";

export default function JournalDrawerToggle() {
  const status = useSessionStore((s) => s.status);

  return (
    <DrawerToggle
      name="journal"
      label="Journal"
      visible={status === "in-progress" || status === "ended"}
    />
  );
}
