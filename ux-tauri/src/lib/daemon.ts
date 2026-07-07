// Frontend transport to the C# daemon, via the Rust `rpc_call` bridge.
// Method names are the exact `IDriverDaemon` (PascalCase) names; args are
// passed positionally (StreamJsonRpc marshals proxy args as a JSON array).

import { invoke } from "@tauri-apps/api/core";
import { listen, type UnlistenFn } from "@tauri-apps/api/event";
import type {
  AppInfo,
  DebugReportData,
  DiagnosticInfo,
  LogMessage,
  PluginMetadata,
  PluginTypeCatalog,
  VirtualScreenInfo,
  VMultiDeviceStatus,
  SerializedDeviceEndpoint,
  SerializedUpdateInfo,
  Settings,
  TabletReference,
} from "./types";

/** Low-level: invoke a daemon method by name with positional args. */
export async function rpc<T = unknown>(method: string, ...params: unknown[]): Promise<T> {
  return invoke<T>("rpc_call", { method, params });
}

export function daemonConnected(): Promise<boolean> {
  return invoke<boolean>("daemon_connected");
}

// ---- events (daemon -> client notifications) -----------------------------

export interface DaemonNotify {
  method: string;
  params: unknown[];
}

export function onNotify(cb: (n: DaemonNotify) => void): Promise<UnlistenFn> {
  return listen<DaemonNotify>("daemon://notify", (e) => cb(e.payload));
}

export function onConnectionChange(cb: (connected: boolean) => void): Promise<UnlistenFn> {
  return listen<boolean>("daemon://connection", (e) => cb(e.payload));
}

/** Subscribe to a single named daemon event, unwrapping its first param arg. */
export function onEvent<T>(name: string, cb: (arg: T) => void): Promise<UnlistenFn> {
  return onNotify((n) => {
    if (n.method === name) cb((n.params?.[0] as T) ?? (undefined as T));
  });
}

// ---- typed IDriverDaemon surface -----------------------------------------

export const daemon = {
  // settings
  getSettings: () => rpc<Settings>("GetSettings"),
  setSettings: (settings: Settings) => rpc<void>("SetSettings", settings),
  resetSettings: () => rpc<void>("ResetSettings"),

  // tablets / devices
  getTablets: () => rpc<TabletReference[]>("GetTablets"),
  detectTablets: () => rpc<TabletReference[]>("DetectTablets"),
  getDevices: () => rpc<SerializedDeviceEndpoint[]>("GetDevices"),
  requestDeviceString: (vendorID: number, productID: number, index: number) =>
    rpc<string>("RequestDeviceString", vendorID, productID, index),

  // debug / logging
  setTabletDebug: (enabled: boolean) => rpc<void>("SetTabletDebug", enabled),
  getCurrentLog: () => rpc<LogMessage[]>("GetCurrentLog"),
  writeMessage: (message: LogMessage) => rpc<void>("WriteMessage", message),
  getDiagnosticInfo: () => rpc<DiagnosticInfo>("GetDiagnosticInfo"),

  // plugins
  loadPlugins: () => rpc<void>("LoadPlugins"),
  installPlugin: (filePath: string) => rpc<boolean>("InstallPlugin", filePath),
  uninstallPlugin: (friendlyName: string) => rpc<boolean>("UninstallPlugin", friendlyName),
  downloadPlugin: (metadata: PluginMetadata) => rpc<boolean>("DownloadPlugin", metadata),
  getLoadedPlugins: () => rpc<PluginMetadata[]>("GetLoadedPlugins"),
  getPluginMetadataRepository: () => rpc<PluginMetadata[]>("GetPluginMetadataRepository"),

  // app / updates
  getApplicationInfo: () => rpc<AppInfo>("GetApplicationInfo"),
  checkForUpdates: () => rpc<SerializedUpdateInfo | null>("CheckForUpdates"),
  installUpdate: () => rpc<void>("InstallUpdate"),
  forceResynchronize: () => rpc<void>("ForceResynchronize"),

  // ---- additive methods (to be added daemon-side, see plan) ----
  getPluginTypes: () => rpc<PluginTypeCatalog>("GetPluginTypes"),
  getVirtualScreen: () => rpc<VirtualScreenInfo>("GetVirtualScreen"),
  getVMultiDeviceStatus: () => rpc<VMultiDeviceStatus>("GetVMultiDeviceStatus"),
  getPresets: () => rpc<string[]>("GetPresets"),
  savePreset: (name: string, settings: Settings) => rpc<void>("SavePreset", name, settings),
  applyPreset: (name: string) => rpc<void>("ApplyPreset", name),
};

// ---- named event helpers (see IDriverDaemon events) ----------------------

export const events = {
  onMessage: (cb: (m: LogMessage) => void) => onEvent<LogMessage>("Message", cb),
  onDeviceReport: (cb: (d: DebugReportData) => void) => onEvent<DebugReportData>("DeviceReport", cb),
  onTabletsChanged: (cb: (t: TabletReference[]) => void) =>
    onNotify((n) => {
      if (n.method === "TabletsChanged") cb((n.params?.[0] as TabletReference[]) ?? []);
    }),
  onResynchronize: (cb: () => void) => onEvent<unknown>("Resynchronize", () => cb()),
};
