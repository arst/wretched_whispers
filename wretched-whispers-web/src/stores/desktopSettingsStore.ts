import { create } from "zustand";

// Toggles the desktop settings form open from the header (desktop build only).
interface DesktopSettingsState {
  open: boolean;
  setOpen: (open: boolean) => void;
}

export const useDesktopSettingsStore = create<DesktopSettingsState>((set) => ({
  open: false,
  setOpen: (open) => set({ open }),
}));
