// Helpers for working with PluginSettingStore (bindings, output modes, filters,
// tools) against the daemon's plugin-type catalog.

import type {
  PluginSetting,
  PluginSettingStore,
  PluginTypeCatalog,
  SerializedPluginType,
} from "./types";

export type PluginCategory = keyof PluginTypeCatalog;

export function findType(
  catalog: PluginTypeCatalog | null,
  category: PluginCategory,
  path: string | null | undefined,
): SerializedPluginType | undefined {
  if (!catalog || !path) return undefined;
  return catalog[category].find((t) => t.Path === path);
}

/** Search every category for a type (used when a store's category is unknown). */
export function findAnyType(
  catalog: PluginTypeCatalog | null,
  path: string | null | undefined,
): SerializedPluginType | undefined {
  if (!catalog || !path) return undefined;
  for (const cat of Object.keys(catalog) as PluginCategory[]) {
    const t = catalog[cat].find((x) => x.Path === path);
    if (t) return t;
  }
  return undefined;
}

/** Build a fresh PluginSettingStore for a catalog type, seeded with defaults. */
export function makeStore(type: SerializedPluginType): PluginSettingStore {
  return {
    Path: type.Path,
    Enable: true,
    Settings: type.Settings.map<PluginSetting>((s) => ({
      Property: s.Property,
      Value: s.Default ?? null,
    })),
  };
}

export function getSetting(store: PluginSettingStore | null | undefined, property: string): unknown {
  return store?.Settings.find((s) => s.Property === property)?.Value;
}

/** Mutate a setting value in place (call inside an immer draft). */
export function setSetting(store: PluginSettingStore, property: string, value: unknown): void {
  const existing = store.Settings.find((s) => s.Property === property);
  if (existing) existing.Value = value;
  else store.Settings.push({ Property: property, Value: value });
}

/** Human label for a binding/plugin store, e.g. "Key Binding: A" or "None". */
export function bindingLabel(
  store: PluginSettingStore | null | undefined,
  catalog: PluginTypeCatalog | null,
): string {
  if (!store || !store.Path) return "None";
  const type = findAnyType(catalog, store.Path);
  const name = type?.Name ?? store.Path.split(".").pop() ?? store.Path;
  const primary = store.Settings.find((s) => s.Value != null && typeof s.Value !== "boolean");
  return primary ? `${name}: ${primary.Value}` : name;
}
