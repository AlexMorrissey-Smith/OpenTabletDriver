import type { PluginSettingStore, SerializedPluginType } from "@/lib/types";
import { getSetting } from "@/lib/plugin";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface Props {
  type: SerializedPluginType | undefined;
  store: PluginSettingStore;
  onSet: (property: string, value: unknown) => void;
}

/** Generic settings form built from a plugin type's schema (the [Property]
 *  members the old Eto PluginSettingStoreEditor rendered by reflection). */
export function PluginSettingsEditor({ type, store, onSet }: Props) {
  if (!type) {
    return (
      <p className="text-sm text-muted-foreground">
        Plugin metadata unavailable (needs the updated daemon). Settings preserved as-is.
      </p>
    );
  }
  if (type.Settings.length === 0) {
    return <p className="text-sm text-muted-foreground">No configurable settings.</p>;
  }

  return (
    <div className="space-y-3">
      {type.Settings.map((s) => {
        const value = getSetting(store, s.Property);
        return (
          <div key={s.Property} className="grid grid-cols-[1fr_auto] items-center gap-3">
            <Label className="text-sm" title={s.ToolTip ?? undefined}>
              {s.DisplayName || s.Property}
              {s.Unit ? <span className="text-muted-foreground"> ({s.Unit})</span> : null}
            </Label>
            <div className="min-w-40 justify-self-end">
              {s.Kind === "bool" ? (
                <Switch
                  checked={Boolean(value)}
                  onCheckedChange={(v) => onSet(s.Property, v)}
                />
              ) : s.Kind === "enum" ? (
                <Select
                  value={value != null ? String(value) : ""}
                  onValueChange={(v) => onSet(s.Property, v)}
                >
                  <SelectTrigger className="w-40">
                    <SelectValue placeholder="Select…" />
                  </SelectTrigger>
                  <SelectContent>
                    {s.EnumValues?.map((e) => (
                      <SelectItem key={e} value={e}>
                        {e}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              ) : s.Kind === "number" ? (
                <Input
                  type="number"
                  className="w-40"
                  value={value != null ? String(value) : ""}
                  min={s.Min ?? undefined}
                  max={s.Max ?? undefined}
                  onChange={(e) =>
                    onSet(s.Property, e.target.value === "" ? null : Number(e.target.value))
                  }
                />
              ) : (
                <Input
                  className="w-40"
                  value={value != null ? String(value) : ""}
                  onChange={(e) => onSet(s.Property, e.target.value)}
                />
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
