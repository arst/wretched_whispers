import { create } from "zustand";

// Toggles the standalone settings form open from the header.
interface DesktopSettingsState {
  open: boolean;
  setOpen: (open: boolean) => void;
}

export const useDesktopSettingsStore = create<DesktopSettingsState>((set) => ({
  open: false,
  setOpen: (open) => set({ open }),
}));
