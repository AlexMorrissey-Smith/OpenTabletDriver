import { useEffect, useState } from "react";
import { currentProfile, useStore } from "@/lib/store";
import { bindingLabel, findType, makeStore, setSetting } from "@/lib/plugin";
import { cn } from "@/lib/utils";
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

/** A single binding cell: shows the current binding, opens a picker on click.
 *  Right-click offers editing the same slot for any configured app override. */
export function BindingButton({ label, store, onChange }: Props) {
  const catalog = useStore((s) => s.catalog);
  const profile = useStore(currentProfile);
  const selectedApp = useStore((s) => s.selectedApp);
  const selectApp = useStore((s) => s.selectApp);
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<PluginSettingStore | null>(null);
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);
  const [pendingOpen, setPendingOpen] = useState(false);

  const appTargets = profile?.AppBindings ?? [];

  function openPicker() {
    setDraft(store ? structuredClone(store) : null);
    setOpen(true);
  }

  // After a context-menu pick switches the app, wait for the re-render so the
  // `store` prop points at the chosen app's binding before opening the editor.
  useEffect(() => {
    if (pendingOpen) {
      setPendingOpen(false);
      setDraft(store ? structuredClone(store) : null);
      setOpen(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingOpen, store]);

  function pickTarget(app: string | null) {
    setMenu(null);
    selectApp(app);
    setPendingOpen(true);
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

  const bound = !!store;

  return (
    <>
      <Button
        variant="outline"
        className="h-10 w-full justify-start gap-2 px-3 font-normal"
        onClick={openPicker}
        onContextMenu={(e) => {
          if (appTargets.length === 0) return;
          e.preventDefault();
          setMenu({ x: e.clientX, y: e.clientY });
        }}
      >
        {label ? <span className="shrink-0 text-muted-foreground">{label}:</span> : null}
        <span className={cn("truncate", !bound && "text-muted-foreground/60")}>
          {bindingLabel(store, catalog)}
        </span>
      </Button>

      {menu ? (
        <div
          className="fixed inset-0 z-50"
          onClick={() => setMenu(null)}
          onContextMenu={(e) => {
            e.preventDefault();
            setMenu(null);
          }}
        >
          <div
            className="absolute min-w-48 rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
            style={{ left: menu.x, top: menu.y }}
          >
            <div className="px-2 py-1 text-xs text-muted-foreground select-none">
              Edit this binding for
            </div>
            {[{ id: null as string | null, name: "All Applications" }, ...appTargets.map((a) => ({ id: a.BundleIdentifier as string | null, name: a.DisplayName }))].map(
              (t) => (
                <button
                  key={t.id ?? "__all__"}
                  className="flex w-full items-center rounded px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                  onClick={(e) => {
                    e.stopPropagation();
                    pickTarget(t.id);
                  }}
                >
                  <span className="truncate">{t.name}</span>
                  {(t.id ?? null) === selectedApp ? (
                    <span className="ml-auto pl-2 text-muted-foreground">current</span>
                  ) : null}
                </button>
              ),
            )}
          </div>
        </div>
      ) : null}

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {label ? `${label} binding` : "Binding"}
              {selectedApp ? (
                <span className="font-normal text-muted-foreground">
                  {" — "}
                  {appTargets.find((a) => a.BundleIdentifier === selectedApp)?.DisplayName ??
                    selectedApp}
                </span>
              ) : null}
            </DialogTitle>
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
