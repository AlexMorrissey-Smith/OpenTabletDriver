import { useStore } from "@/lib/store";
import { findType, makeStore, setSetting, type PluginCategory } from "@/lib/plugin";
import type { PluginSettingStoreCollection } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { Card, CardContent } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PluginSettingsEditor } from "./PluginSettingsEditor";

interface Props {
  category: PluginCategory; // "Tools" | "Filters"
  items: PluginSettingStoreCollection;
  mutate: (fn: (items: PluginSettingStoreCollection) => void) => void;
}

export function PluginCollectionEditor({ category, items, mutate }: Props) {
  const catalog = useStore((s) => s.catalog);
  const available = catalog?.[category] ?? [];

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Select
          value=""
          onValueChange={(path) => {
            const type = findType(catalog, category, path);
            if (type) mutate((list) => void list.push(makeStore(type)));
          }}
        >
          <SelectTrigger className="w-64">
            <SelectValue placeholder={`Add ${category === "Tools" ? "tool" : "filter"}…`} />
          </SelectTrigger>
          <SelectContent>
            {available.length === 0 ? (
              <SelectItem value="__none__" disabled>
                {catalog ? "None available" : "Needs updated daemon"}
              </SelectItem>
            ) : (
              available.map((t) => (
                <SelectItem key={t.Path} value={t.Path}>
                  {t.Name}
                </SelectItem>
              ))
            )}
          </SelectContent>
        </Select>
      </div>

      {items.length === 0 ? (
        <p className="text-sm text-muted-foreground">None configured.</p>
      ) : (
        items.map((store, i) => {
          const type = findType(catalog, category, store.Path);
          return (
            <Card key={i}>
              <CardContent className="space-y-3 pt-6">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <Switch
                      checked={store.Enable}
                      onCheckedChange={(v) => mutate((list) => void (list[i].Enable = v))}
                    />
                    <span className="font-medium">{type?.Name ?? store.Path.split(".").pop()}</span>
                  </div>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => mutate((list) => void list.splice(i, 1))}
                  >
                    Remove
                  </Button>
                </div>
                <PluginSettingsEditor
                  type={type}
                  store={store}
                  onSet={(prop, val) => mutate((list) => setSetting(list[i], prop, val))}
                />
              </CardContent>
            </Card>
          );
        })
      )}
    </div>
  );
}
