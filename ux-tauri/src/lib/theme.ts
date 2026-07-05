import { create } from "zustand";

export type Theme = "light" | "dark" | "system";
const KEY = "otd-theme";
const mql = window.matchMedia("(prefers-color-scheme: dark)");

function resolvedDark(theme: Theme): boolean {
  return theme === "dark" || (theme === "system" && mql.matches);
}

function apply(theme: Theme) {
  document.documentElement.classList.toggle("dark", resolvedDark(theme));
}

const initial = (localStorage.getItem(KEY) as Theme) || "system";
apply(initial);

// Follow the OS when in "system" mode.
mql.addEventListener("change", () => {
  if (((localStorage.getItem(KEY) as Theme) || "system") === "system") apply("system");
});

interface ThemeState {
  theme: Theme;
  setTheme: (t: Theme) => void;
}

export const useTheme = create<ThemeState>((set) => ({
  theme: initial,
  setTheme: (t) => {
    localStorage.setItem(KEY, t);
    apply(t);
    set({ theme: t });
  },
}));
