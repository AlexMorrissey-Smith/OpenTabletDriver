// Wacom/Gaomon-style pressure test: a black box you draw in with the pen —
// the harder you press, the thicker the stroke. Works like a drawing app:
// strokes land under the cursor (pointer events give position), while stroke
// width comes from the daemon's raw pressure stream (true hardware values,
// enabled only while mounted).

import { useEffect, useRef } from "react";
import { daemon, events } from "@/lib/daemon";
import { useStore } from "@/lib/store";
import { Button } from "@/components/ui/button";

const MAX_WIDTH = 28; // stroke px at full pressure

export function PressureTester() {
  const tablets = useStore((s) => s.tablets);
  const selectedTablet = useStore((s) => s.selectedTablet);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const last = useRef<{ x: number; y: number } | null>(null);
  const rawPressure = useRef(0); // 0..1 from the debug stream

  const tablet = tablets.find((t) => t.Properties?.Name === selectedTablet) ?? tablets[0];
  const maxPressure = Number(tablet?.Properties.Specifications?.Pen?.MaxPressure) || 8191;

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    // Match the backing store to the on-screen size once on mount.
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    const ctx = canvas.getContext("2d")!;
    ctx.scale(dpr, dpr);
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    ctx.strokeStyle = "#fff";
    ctx.fillStyle = "#fff";

    let unlisten: (() => void) | undefined;
    let cancelled = false;

    daemon.setTabletDebug(true).catch(() => {});
    events
      .onDeviceReport((r) => {
        const value = Number((r.Data as { Pressure?: unknown })?.Pressure);
        if (Number.isFinite(value))
          rawPressure.current = Math.min(1, Math.max(0, value / maxPressure));
      })
      .then((u) => {
        if (cancelled) u();
        else unlisten = u;
      });

    return () => {
      cancelled = true;
      unlisten?.();
      daemon.setTabletDebug(false).catch(() => {});
      last.current = null;
    };
  }, [maxPressure]);

  function draw(e: React.PointerEvent<HTMLCanvasElement>) {
    if ((e.buttons & 1) === 0) {
      last.current = null; // pen lifted / button up: end the stroke
      return;
    }

    const canvas = canvasRef.current!;
    const ctx = canvas.getContext("2d")!;
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    // Hardware pressure when the stream has it; pointer pressure as fallback
    // (also lets a plain mouse scribble at constant width).
    const p = rawPressure.current > 0 ? rawPressure.current : e.pressure || 0.5;
    const width = Math.max(1, p * MAX_WIDTH);

    if (last.current) {
      ctx.lineWidth = width;
      ctx.beginPath();
      ctx.moveTo(last.current.x, last.current.y);
      ctx.lineTo(x, y);
      ctx.stroke();
    } else {
      ctx.beginPath();
      ctx.arc(x, y, width / 2, 0, Math.PI * 2);
      ctx.fill();
    }
    last.current = { x, y };
  }

  function clear() {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d")!;
    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.restore();
    last.current = null;
  }

  return (
    <div className="space-y-2">
      <canvas
        ref={canvasRef}
        className="h-64 w-full touch-none select-none rounded-md border border-border bg-black"
        onPointerDown={draw}
        onPointerMove={draw}
        onPointerUp={() => (last.current = null)}
        onPointerLeave={() => (last.current = null)}
      />
      <div className="flex">
        <Button variant="ghost" size="sm" className="ml-auto" onClick={clear}>
          Clear
        </Button>
      </div>
    </div>
  );
}
