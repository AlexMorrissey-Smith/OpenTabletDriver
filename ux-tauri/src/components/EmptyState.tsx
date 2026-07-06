import { useState } from "react";
import { daemon } from "@/lib/daemon";
import { useStore } from "@/lib/store";
import { Button } from "@/components/ui/button";
import { Bluetooth, RefreshCw, Tablet, Usb } from "lucide-react";

/** Shown instead of a settings page when no tablet is configured yet. */
export function NoTabletState() {
  const reload = useStore((s) => s.reload);
  const [busy, setBusy] = useState(false);

  async function detect() {
    setBusy(true);
    try {
      await daemon.detectTablets();
      await reload();
    } catch {
      // daemon offline; the connection screen takes over
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex h-full flex-col items-center justify-center gap-6 text-center select-none">
      <div className="flex size-16 items-center justify-center rounded-2xl bg-muted">
        <Tablet className="size-8 text-muted-foreground" />
      </div>
      <div className="space-y-1">
        <h2 className="text-lg font-semibold">No tablet detected</h2>
        <p className="max-w-sm text-sm text-muted-foreground">
          Connect your tablet and it will appear here automatically.
        </p>
      </div>
      <div className="flex gap-6 text-xs text-muted-foreground">
        <span className="flex items-center gap-1.5">
          <Usb className="size-3.5" /> Plug in over USB
        </span>
        <span className="flex items-center gap-1.5">
          <Bluetooth className="size-3.5" /> Pair in system Bluetooth settings
        </span>
      </div>
      <Button variant="outline" size="sm" onClick={detect} disabled={busy}>
        <RefreshCw className={busy ? "size-4 animate-spin" : "size-4"} />
        {busy ? "Detecting…" : "Detect tablets"}
      </Button>
    </div>
  );
}
