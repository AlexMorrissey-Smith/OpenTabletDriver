import { useState } from "react";
import { useStore } from "@/lib/store";
import { bindingLabel, findType, makeStore, setSetting } from "@/lib/plugin";
import type { PluginSettingStore } from "@/lib/types";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PluginSettingsEditor } from "./PluginSettingsEditor";

interface Props {
  label?: string;
  store: PluginSettingStore | null | undefined;
  onChange: (store: PluginSettingStore | null) => void;
}

/** A single binding cell: shows the current binding, opens a picker on click. */
export function BindingButton({ label, store, onChange }: Props) {
  const catalog = useStore((s) => s.catalog);
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<PluginSettingStore | null>(null);

  function openPicker() {
    setDraft(store ? structuredClone(store) : null);
    setOpen(true);
  }

  function selectType(path: string | null) {
    if (!path || path === "__none__") {
      setDraft(null);
      return;
    }
    const type = findType(catalog, "Bindings", path);
    if (type) setDraft(makeStore(type));
  }

  function set(property: string, value: unknown) {
    setDraft((d) => {
      if (!d) return d;
      const next = structuredClone(d);
      setSetting(next, property, value);
      return next;
    });
  }

  const draftType = findType(catalog, "Bindings", draft?.Path);

  return (
    <>
      <Button
        variant="outline"
        className="w-full justify-start font-normal"
        onClick={openPicker}
      >
        {label ? <span className="text-muted-foreground mr-2">{label}:</span> : null}
        <span className="truncate">{bindingLabel(store, catalog)}</span>
      </Button>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{label ? `${label} binding` : "Binding"}</DialogTitle>
          </DialogHeader>

          {!catalog ? (
            <p className="text-sm text-muted-foreground">
              Binding types unavailable — connect to the updated daemon.
            </p>
          ) : (
            <div className="space-y-4">
              <Select value={draft?.Path ?? "__none__"} onValueChange={selectType}>
                <SelectTrigger>
                  <SelectValue placeholder="Binding type">
                    {(v: string) =>
                      v === "__none__"
                        ? "None"
                        : (catalog.Bindings.find((t) => t.Path === v)?.Name ?? v)
                    }
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__none__">None</SelectItem>
                  {catalog.Bindings.map((t) => (
                    <SelectItem key={t.Path} value={t.Path}>
                      {t.Name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              {draft ? (
                <PluginSettingsEditor type={draftType} store={draft} onSet={set} />
              ) : null}
            </div>
          )}

          <DialogFooter>
            <Button
              variant="ghost"
              onClick={() => {
                onChange(null);
                setOpen(false);
              }}
            >
              Clear
            </Button>
            <Button
              onClick={() => {
                onChange(draft);
                setOpen(false);
              }}
            >
              Apply
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
