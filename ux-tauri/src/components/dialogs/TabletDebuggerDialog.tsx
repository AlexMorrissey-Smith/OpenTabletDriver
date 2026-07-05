import { useEffect, useRef, useState } from "react";
import { daemon, events } from "@/lib/daemon";
import { useDialog } from "@/lib/ui";
import type { DebugReportData } from "@/lib/types";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";

export function TabletDebuggerDialog() {
  const d = useDialog("debugger");
  const [latest, setLatest] = useState<DebugReportData | null>(null);
  const [count, setCount] = useState(0);
  const [rate, setRate] = useState(0);
  const times = useRef<number[]>([]);

  useEffect(() => {
    if (!d.open) return;
    let unlisten: (() => void) | undefined;
    let cancelled = false;

    daemon.setTabletDebug(true).catch(() => {});
    events
      .onDeviceReport((r) => {
        setLatest(r);
        setCount((c) => c + 1);
        const now = performance.now();
        times.current.push(now);
        times.current = times.current.filter((t) => now - t < 1000);
        setRate(times.current.length);
      })
      .then((u) => {
        if (cancelled) u();
        else unlisten = u;
      });

    return () => {
      cancelled = true;
      unlisten?.();
      daemon.setTabletDebug(false).catch(() => {});
      times.current = [];
    };
  }, [d.open]);

  return (
    <Dialog {...d}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Tablet Debugger</DialogTitle>
        </DialogHeader>
        <div className="space-y-3 text-sm">
          <div className="flex gap-6">
            <Stat label="Reports" value={count} />
            <Stat label="Rate" value={`${rate} Hz`} />
            <Stat label="Device" value={latest?.Tablet?.Properties?.Name ?? "—"} />
          </div>
          <div>
            <div className="mb-1 text-xs text-muted-foreground">Latest report</div>
            <pre className="max-h-80 overflow-auto rounded-md border bg-muted/30 p-2 text-xs">
              {latest ? JSON.stringify(latest.Data, null, 1) : "Waiting for reports…"}
            </pre>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function Stat({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="font-mono text-base">{value}</div>
    </div>
  );
}
