import { create } from "zustand";
import { immer } from "zustand/middleware/immer";
import { daemon, daemonConnected, events, onConnectionChange } from "./daemon";
import type {
  BindingSettings,
  LogMessage,
  PluginTypeCatalog,
  Profile,
  Settings,
  TabletReference,
} from "./types";

// Debounced autosave — ports SettingsAutosaveService.cs. Any edit mutates the
// in-memory Settings immediately (snappy UI) and pushes the whole Settings to
// the daemon a beat later.
let saveTimer: ReturnType<typeof setTimeout> | null = null;
let initialized = false; // guard against React StrictMode double-invocation

interface Store {
  connected: boolean;
  ready: boolean;
  error: string | null;
  settings: Settings | null;
  catalog: PluginTypeCatalog | null;
  tablets: TabletReference[];
  selectedTablet: string | null;
  selectedApp: string | null; // AppBindingProfile.BundleIdentifier, or null = "All Applications"
  activePage: string;
  log: LogMessage[];

  init: () => Promise<void>;
  clearLog: () => void;
  reload: () => Promise<void>;
  refreshCatalog: () => Promise<void>;
  selectTablet: (name: string) => void;
  selectApp: (id: string | null) => void;
  setPage: (page: string) => void;
  /** Mutate Settings via an immer draft, then autosave. */
  update: (fn: (s: Settings) => void) => void;
  updateProfile: (fn: (p: Profile) => void) => void;
  updateBindings: (fn: (b: BindingSettings) => void) => void;
  save: () => void;
}

export const useStore = create<Store>()(
  immer((set, get) => ({
    connected: false,
    ready: false,
    error: null,
    settings: null,
    catalog: null,
    tablets: [],
    selectedTablet: null,
    selectedApp: null,
    activePage: "tablet",
    log: [],

    async init() {
      if (initialized) return;
      initialized = true;
      onConnectionChange((c) => {
        set((s) => {
          s.connected = c;
        });
        if (c) get().reload();
      });
      events.onTabletsChanged(() => get().reload());
      events.onResynchronize(() => get().reload());
      events.onMessage((m) => {
        set((s) => {
          s.log.push(m);
          if (s.log.length > 1000) s.log.splice(0, s.log.length - 1000);
        });
      });
      daemonConnected().then((c) => set((s) => void (s.connected = c)));
      await get().reload();
    },

    clearLog() {
      set((s) => {
        s.log = [];
      });
    },

    async refreshCatalog() {
      const c = await daemon.getPluginTypes().catch(() => null);
      if (c)
        set((s) => {
          s.catalog = c;
        });
    },

    async reload() {
      try {
        const [settings, tablets] = await Promise.all([daemon.getSettings(), daemon.getTablets()]);
        // Plugin catalog needs the rebuilt daemon (GetPluginTypes); tolerate absence.
        let catalog = get().catalog;
        if (!catalog) {
          catalog = await daemon.getPluginTypes().catch(() => null);
        }
        if (get().log.length === 0) {
          const initialLog = await daemon.getCurrentLog().catch(() => []);
          set((s) => {
            s.log = initialLog;
          });
        }
        set((s) => {
          s.settings = settings;
          s.tablets = tablets;
          if (catalog) s.catalog = catalog;
          if (!s.selectedTablet || !settings.Profiles.some((p) => p.Tablet === s.selectedTablet)) {
            s.selectedTablet = settings.Profiles[0]?.Tablet ?? null;
          }
          s.ready = true;
          s.error = null;
        });
      } catch (e) {
        set((s) => {
          s.error = String(e);
        });
      }
    },

    selectTablet(name) {
      set((s) => {
        s.selectedTablet = name;
        s.selectedApp = null;
      });
    },
    selectApp(id) {
      set((s) => {
        s.selectedApp = id;
      });
    },
    setPage(page) {
      set((s) => {
        s.activePage = page;
      });
    },

    update(fn) {
      set((s) => {
        if (s.settings) fn(s.settings);
      });
      get().save();
    },

    updateProfile(fn) {
      const tablet = get().selectedTablet;
      get().update((s) => {
        const p = s.Profiles.find((x) => x.Tablet === tablet) ?? s.Profiles[0];
        if (p) fn(p);
      });
    },

    updateBindings(fn) {
      const app = get().selectedApp;
      get().updateProfile((p) => {
        if (app == null) {
          fn(p.Bindings);
        } else {
          const override = p.AppBindings?.find((a) => a.BundleIdentifier === app);
          if (override) fn(override.BindingSettings);
        }
      });
    },

    save() {
      if (saveTimer) clearTimeout(saveTimer);
      saveTimer = setTimeout(() => {
        const s = get().settings;
        if (s) daemon.setSettings(s).catch((e) => console.error("[autosave] failed", e));
      }, 400);
    },
  })),
);

// ---- selectors (read-only helpers) ---------------------------------------

export function currentProfile(s: Store): Profile | null {
  if (!s.settings) return null;
  return s.settings.Profiles.find((p) => p.Tablet === s.selectedTablet) ?? s.settings.Profiles[0] ?? null;
}

export function currentBindings(s: Store): BindingSettings | null {
  const p = currentProfile(s);
  if (!p) return null;
  if (s.selectedApp == null) return p.Bindings;
  return p.AppBindings?.find((a) => a.BundleIdentifier === s.selectedApp)?.BindingSettings ?? null;
}
