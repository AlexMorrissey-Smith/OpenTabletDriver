import { create } from "zustand";

export type DialogName =
  | "about"
  | "plugins"
  | "debugger"
  | "strings"
  | "updater"
  | "greeter"
  | null;

interface UIState {
  dialog: DialogName;
  open: (d: DialogName) => void;
  close: () => void;
}

export const useUI = create<UIState>((set) => ({
  dialog: null,
  open: (d) => set({ dialog: d }),
  close: () => set({ dialog: null }),
}));

/** Wire a Dialog's open/onOpenChange to the shared UI dialog slot. */
export function useDialog(name: Exclude<DialogName, null>) {
  const dialog = useUI((s) => s.dialog);
  const close = useUI((s) => s.close);
  return {
    open: dialog === name,
    onOpenChange: (o: boolean) => {
      if (!o) close();
    },
  };
}
