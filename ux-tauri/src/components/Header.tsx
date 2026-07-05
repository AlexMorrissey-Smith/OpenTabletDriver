import { useState } from "react";
import { currentProfile, useStore } from "@/lib/store";
import { daemon } from "@/lib/daemon";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Plus, Minus, RefreshCw } from "lucide-react";

const ALL = "__all__";

export function Header({ children }: { children?: React.ReactNode }) {
  const settings = useStore((s) => s.settings);
  const profile = useStore(currentProfile);
  const selectedTablet = useStore((s) => s.selectedTablet);
  const selectedApp = useStore((s) => s.selectedApp);
  const selectTablet = useStore((s) => s.selectTablet);
  const selectApp = useStore((s) => s.selectApp);
  const updateProfile = useStore((s) => s.updateProfile);

  const [addOpen, setAddOpen] = useState(false);
  const [bundleId, setBundleId] = useState("");
  const [displayName, setDisplayName] = useState("");

  const profiles = settings?.Profiles ?? [];
  const appBindings = profile?.AppBindings ?? [];

  function addApp() {
    if (!bundleId.trim()) return;
    const id = bundleId.trim();
    const name = displayName.trim() || id;
    updateProfile((p) => {
      p.AppBindings = p.AppBindings ?? [];
      p.AppBindings.push({
        BundleIdentifier: id,
        DisplayName: name,
        BindingSettings: structuredClone(p.Bindings),
      });
    });
    selectApp(id);
    setBundleId("");
    setDisplayName("");
    setAddOpen(false);
  }

  function removeApp() {
    if (selectedApp == null) return;
    const target = selectedApp;
    updateProfile((p) => {
      p.AppBindings = (p.AppBindings ?? []).filter((a) => a.BundleIdentifier !== target);
    });
    selectApp(null);
  }

  return (
    <div className="flex flex-wrap items-center gap-3 border-b px-4 py-2">
      {/* Tablet / profile */}
      <Select value={selectedTablet ?? ""} onValueChange={(v) => v && selectTablet(v)}>
        <SelectTrigger className="w-56">
          <SelectValue placeholder="No tablet" />
        </SelectTrigger>
        <SelectContent>
          {profiles.length === 0 ? (
            <SelectItem value="__none__" disabled>
              No tablets detected
            </SelectItem>
          ) : (
            profiles.map((p) => (
              <SelectItem key={p.Tablet} value={p.Tablet}>
                {p.Tablet}
              </SelectItem>
            ))
          )}
        </SelectContent>
      </Select>

      <Button
        variant="outline"
        size="icon"
        title="Detect tablets"
        onClick={() => daemon.detectTablets()}
      >
        <RefreshCw className="size-4" />
      </Button>

      <div className="mx-2 h-6 w-px bg-border" />

      {/* Application-specific bindings */}
      <span className="text-sm text-muted-foreground">Bindings for</span>
      <Select
        value={selectedApp ?? ALL}
        onValueChange={(v) => selectApp(v === ALL ? null : v)}
      >
        <SelectTrigger className="w-56">
          <SelectValue>
            {(v: string) =>
              v === ALL
                ? "All Applications"
                : (appBindings.find((a) => a.BundleIdentifier === v)?.DisplayName ?? v)
            }
          </SelectValue>
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={ALL}>All Applications</SelectItem>
          {appBindings.map((a) => (
            <SelectItem key={a.BundleIdentifier} value={a.BundleIdentifier}>
              {a.DisplayName}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Button
        variant="outline"
        size="icon"
        title="Add application override"
        disabled={!profile}
        onClick={() => setAddOpen(true)}
      >
        <Plus className="size-4" />
      </Button>
      <Button
        variant="outline"
        size="icon"
        title="Remove application override"
        disabled={selectedApp == null}
        onClick={removeApp}
      >
        <Minus className="size-4" />
      </Button>

      {children ? <div className="ml-auto flex items-center gap-1">{children}</div> : null}

      <Dialog open={addOpen} onOpenChange={setAddOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Add application override</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            <div className="space-y-1">
              <Label>Bundle identifier / app path</Label>
              <Input
                value={bundleId}
                placeholder="com.example.app"
                onChange={(e) => setBundleId(e.target.value)}
              />
            </div>
            <div className="space-y-1">
              <Label>Display name</Label>
              <Input
                value={displayName}
                placeholder="Example App"
                onChange={(e) => setDisplayName(e.target.value)}
              />
            </div>
            <p className="text-xs text-muted-foreground">
              Starts as a copy of the current "All Applications" bindings.
            </p>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setAddOpen(false)}>
              Cancel
            </Button>
            <Button onClick={addApp}>Add</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
