// Pressure curve editor — the classic vendor-driver control. Backed by the
// built-in PressureCurveFilter (cubic bézier (0,0)..(1,1), two control points);
// this component just edits the filter's four settings in profile.Filters.

import { useRef } from "react";
import { currentProfile, useStore } from "@/lib/store";
import { findType, getSetting, makeStore, setSetting } from "@/lib/plugin";
import { Switch } from "@/components/ui/switch";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

const FILTER_PATH = "OpenTabletDriver.Desktop.Filters.PressureCurveFilter";

const PRESETS: { name: string; pts: [number, number, number, number] }[] = [
  { name: "Linear", pts: [0.33, 0.33, 0.67, 0.67] },
  { name: "Soft", pts: [0.25, 0.55, 0.55, 0.95] },
  { name: "Firm", pts: [0.45, 0.05, 0.75, 0.45] },
  { name: "S-curve", pts: [0.42, 0.0, 0.58, 1.0] },
];

const SIZE = 220;
const PAD = 10;
const W = SIZE - PAD * 2;

const toPx = (x: number, y: number) => [PAD + x * W, PAD + (1 - y) * W] as const;

export function PressureCurve() {
  const profile = useStore(currentProfile);
  const catalog = useStore((s) => s.catalog);
  const updateProfile = useStore((s) => s.updateProfile);
  const svgRef = useRef<SVGSVGElement>(null);

  const store = profile?.Filters?.find((f) => f?.Path === FILTER_PATH) ?? null;
  const enabled = !!store && store.Enable !== false;

  const x1 = Number(getSetting(store, "X1") ?? 0.33);
  const y1 = Number(getSetting(store, "Y1") ?? 0.33);
  const x2 = Number(getSetting(store, "X2") ?? 0.67);
  const y2 = Number(getSetting(store, "Y2") ?? 0.67);

  function setEnabled(on: boolean) {
    updateProfile((p) => {
      p.Filters = p.Filters ?? [];
      const i = p.Filters.findIndex((f) => f?.Path === FILTER_PATH);
      if (on && i < 0) {
        const type = findType(catalog, "Filters", FILTER_PATH);
        if (type) p.Filters.push(makeStore(type));
      } else if (!on && i >= 0) {
        p.Filters.splice(i, 1);
      }
    });
  }

  function setPoints(nx1: number, ny1: number, nx2: number, ny2: number) {
    updateProfile((p) => {
      const s = p.Filters?.find((f) => f?.Path === FILTER_PATH);
      if (!s) return;
      setSetting(s, "X1", round(nx1));
      setSetting(s, "Y1", round(ny1));
      setSetting(s, "X2", round(nx2));
      setSetting(s, "Y2", round(ny2));
    });
  }

  function dragHandle(e: React.PointerEvent) {
    if (!enabled) return;
    (e.target as Element).setPointerCapture(e.pointerId);
  }

  function moveHandle(which: 1 | 2, e: React.PointerEvent) {
    if (!enabled || e.buttons !== 1) return;
    const rect = svgRef.current!.getBoundingClientRect();
    const x = clamp01((e.clientX - rect.left - PAD) / W);
    const y = clamp01(1 - (e.clientY - rect.top - PAD) / W);
    if (which === 1) setPoints(x, y, x2, y2);
    else setPoints(x1, y1, x, y);
  }

  const [p0x, p0y] = toPx(0, 0);
  const [c1x, c1y] = toPx(x1, y1);
  const [c2x, c2y] = toPx(x2, y2);
  const [p3x, p3y] = toPx(1, 1);

  const isPreset = (pts: [number, number, number, number]) =>
    enabled &&
    Math.abs(pts[0] - x1) < 0.01 &&
    Math.abs(pts[1] - y1) < 0.01 &&
    Math.abs(pts[2] - x2) < 0.01 &&
    Math.abs(pts[3] - y2) < 0.01;

  return (
    <div className="flex items-start gap-6">
      <svg
        ref={svgRef}
        width={SIZE}
        height={SIZE}
        className={cn(
          "shrink-0 rounded-md border bg-muted/30",
          !enabled && "opacity-40",
        )}
      >
        {/* quarter grid */}
        {[0.25, 0.5, 0.75].map((f) => (
          <g key={f} className="stroke-border/60">
            <line x1={PAD + f * W} y1={PAD} x2={PAD + f * W} y2={PAD + W} />
            <line x1={PAD} y1={PAD + f * W} x2={PAD + W} y2={PAD + f * W} />
          </g>
        ))}
        {/* linear reference */}
        <line x1={p0x} y1={p0y} x2={p3x} y2={p3y} strokeDasharray="3 3" className="stroke-border" />
        {/* handle stems */}
        {enabled ? (
          <>
            <line x1={p0x} y1={p0y} x2={c1x} y2={c1y} className="stroke-primary/40" />
            <line x1={p3x} y1={p3y} x2={c2x} y2={c2y} className="stroke-primary/40" />
          </>
        ) : null}
        {/* the curve */}
        <path
          d={`M ${p0x} ${p0y} C ${c1x} ${c1y}, ${c2x} ${c2y}, ${p3x} ${p3y}`}
          fill="none"
          strokeWidth={2}
          className="stroke-primary"
        />
        {/* draggable control points */}
        {enabled ? (
          <>
            <circle
              cx={c1x}
              cy={c1y}
              r={7}
              className="cursor-grab fill-background stroke-primary stroke-2"
              onPointerDown={dragHandle}
              onPointerMove={(e) => moveHandle(1, e)}
            />
            <circle
              cx={c2x}
              cy={c2y}
              r={7}
              className="cursor-grab fill-background stroke-primary stroke-2"
              onPointerDown={dragHandle}
              onPointerMove={(e) => moveHandle(2, e)}
            />
          </>
        ) : null}
        {/* axes hint */}
        <text x={PAD + W / 2} y={SIZE - 1} textAnchor="middle" className="fill-muted-foreground text-[9px] select-none">
          pen pressure →
        </text>
      </svg>

      <div className="space-y-4">
        <div className="flex items-center gap-3">
          <Switch checked={enabled} onCheckedChange={setEnabled} />
          <span className="text-sm">{enabled ? "Custom curve" : "Default (linear)"}</span>
        </div>
        <div className="flex flex-wrap gap-2">
          {PRESETS.map((preset) => (
            <Button
              key={preset.name}
              variant={isPreset(preset.pts) ? "default" : "outline"}
              size="sm"
              disabled={!enabled}
              onClick={() => setPoints(...preset.pts)}
            >
              {preset.name}
            </Button>
          ))}
        </div>
        <p className="max-w-56 text-xs text-muted-foreground">
          Drag the two handles to shape how tip pressure translates to output
          pressure. Above the dashed line = lighter touch, below = firmer.
        </p>
      </div>
    </div>
  );
}

const clamp01 = (v: number) => Math.min(1, Math.max(0, v));
const round = (v: number) => Math.round(v * 100) / 100;
