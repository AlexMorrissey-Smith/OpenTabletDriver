// Wire types mirroring the C# daemon DTOs (Newtonsoft.Json => PascalCase).
// These are compile-time only; the raw JSON round-trips losslessly regardless,
// so unknown/extra fields on a PluginSettingStore etc. are preserved on write.
// Refine against real `GetSettings` output as the UI is built out.

// LogLevel serializes as an integer on the wire (Newtonsoft default enum handling).
export enum LogLevel {
  Debug = 0,
  Verbose = 1,
  Info = 2,
  Warning = 3,
  Error = 4,
  Fatal = 5,
}

export interface LogMessage {
  Time: string;
  Group: string;
  Message: string;
  StackTrace?: string | null;
  Level: number;
  Notification: boolean;
}

/** A configured plugin instance: a type path + its serialized settings. */
export interface PluginSettingStore {
  Path: string;
  Settings: PluginSetting[];
  Enable: boolean;
  [k: string]: unknown;
}

export interface PluginSetting {
  Property: string;
  Value: unknown;
  [k: string]: unknown;
}

export type PluginSettingStoreCollection = PluginSettingStore[];

export interface AreaSettings {
  Width: number;
  Height: number;
  X: number;
  Y: number;
  Rotation: number;
}

/** One monitor's rect in the driver's virtual-screen coordinate space. */
export interface DisplayInfo {
  Index: number;
  X: number;
  Y: number;
  Width: number;
  Height: number;
}

/** The whole desktop plus its individual monitors (from the daemon, NOT the
 *  webview's own monitor list — only this matches the driver's mapping). */
export interface VirtualScreenInfo {
  X: number;
  Y: number;
  Width: number;
  Height: number;
  Displays: DisplayInfo[];
}

/** Install state of the external VMulti driver (Windows Ink output mode). */
export interface VMultiDeviceStatus {
  Kind: number; // 0 Ready, 1 Missing, 2 Incomplete, 3 OpenFailed
  IsAvailable: boolean;
  IsExtendedDigitizerAvailable: boolean;
  Message: string;
  DownloadUrl: string;
}

export interface AbsoluteModeSettings {
  Display?: AreaSettings;
  Tablet?: AreaSettings;
  /** Per display-layout remembered areas, keyed by a layout signature string. */
  DisplayLayouts?: Record<string, AreaSettings>;
  EnableClipping?: boolean;
  EnableAreaLimiting?: boolean;
  LockAspectRatio?: boolean;
  [k: string]: unknown;
}

export interface RelativeModeSettings {
  XSensitivity: number;
  YSensitivity: number;
  RelativeRotation: number;
  RelativeResetDelay: string; // TimeSpan, e.g. "00:00:00.1000000"
  [k: string]: unknown;
}

export interface WheelBindingSettings {
  ClockwiseRotation?: PluginSettingStore | null;
  CounterClockwiseRotation?: PluginSettingStore | null;
  ClockwiseActivationThreshold: number;
  CounterClockwiseActivationThreshold: number;
  WheelButtons: PluginSettingStoreCollection;
  [k: string]: unknown;
}

export interface BindingSettings {
  TipActivationThreshold: number;
  TipButton?: PluginSettingStore | null;
  EraserActivationThreshold: number;
  EraserButton?: PluginSettingStore | null;
  PenButtons: PluginSettingStoreCollection;
  AuxButtons: PluginSettingStoreCollection;
  MouseButtons: PluginSettingStoreCollection;
  MouseScrollUp?: PluginSettingStore | null;
  MouseScrollDown?: PluginSettingStore | null;
  WheelBindings: WheelBindingSettings[];
  DisablePressure: boolean;
  DisableTilt: boolean;
  EnableDragBindings: boolean;
  [k: string]: unknown;
}

/** Per-application binding override (Wacom-style app-specific bindings). */
export interface AppBindingProfile {
  BundleIdentifier: string; // bundle id (macOS) or app path (Win/Linux)
  DisplayName: string;
  BindingSettings: BindingSettings;
  [k: string]: unknown;
}

export interface Profile {
  Tablet: string;
  OutputMode: PluginSettingStore;
  Filters: PluginSettingStoreCollection;
  AbsoluteModeSettings?: AbsoluteModeSettings | null;
  RelativeModeSettings?: RelativeModeSettings | null;
  /** Wire key is `Bindings` (confirmed against live daemon). */
  Bindings: BindingSettings;
  /** Present only when app-specific overrides exist; may be omitted when empty. */
  AppBindings?: AppBindingProfile[];
  [k: string]: unknown;
}

export interface Settings {
  Profiles: Profile[];
  Tools: PluginSettingStoreCollection;
  LockUsableAreaDisplay: boolean;
  LockUsableAreaTablet: boolean;
  Revision?: string;
  [k: string]: unknown;
}

export interface DeviceIdentifier {
  VendorID: number;
  ProductID: number;
  [k: string]: unknown;
}

export interface TabletReference {
  Properties: {
    Name: string;
    [k: string]: unknown;
  };
  Identifiers: DeviceIdentifier[];
  [k: string]: unknown;
}

export interface SerializedDeviceEndpoint {
  DevicePath: string;
  Manufacturer?: string | null;
  ProductName?: string | null;
  SerialNumber?: string | null;
  FriendlyName?: string | null;
  VendorID: number;
  ProductID: number;
  InputReportLength: number;
  OutputReportLength: number;
  FeatureReportLength: number;
  CanOpen: boolean;
  DeviceAttributes: Record<string, string>;
}

export interface DebugReportData {
  Tablet: TabletReference;
  Path: string;
  Data: unknown; // raw decoded report (JToken)
}

export interface PluginMetadata {
  Name: string;
  Owner?: string;
  Description?: string;
  RepositoryUrl?: string;
  PluginVersion?: string;
  SupportedDriverVersion?: string;
  [k: string]: unknown;
}

export interface SerializedUpdateInfo {
  Version: string;
  [k: string]: unknown;
}

export interface DiagnosticInfo {
  [k: string]: unknown;
}

export interface AppInfo {
  [k: string]: unknown;
}

/** Catalog from the additive `GetPluginTypes` RPC — mirrors the daemon's
 *  SerializedPluginType / SerializedPluginSetting (Contracts/PluginTypeCatalog.cs). */
export interface SerializedPluginSetting {
  Property: string;
  DisplayName: string;
  Kind: "bool" | "number" | "string" | "enum";
  Default?: unknown;
  EnumValues?: string[] | null;
  Min?: number | null;
  Max?: number | null;
  ToolTip?: string | null;
  Unit?: string | null;
}

export interface SerializedPluginType {
  Path: string; // full type name (goes in PluginSettingStore.Path)
  Name: string; // friendly name
  Settings: SerializedPluginSetting[];
}

export interface PluginTypeCatalog {
  Bindings: SerializedPluginType[];
  OutputModes: SerializedPluginType[];
  Filters: SerializedPluginType[];
  Tools: SerializedPluginType[];
}
