import { useRef, useState } from "react";
import type { AreaSettings } from "@/lib/types";
import { clampArea, fullArea, resizeArea, type ResizeMode } from "@/lib/area";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Maximize2 } from "lucide-react";

type Which = "display" | "tablet";
type Mode = "move" | ResizeMode;
interface Frame {
  full: { w: number; h: number };
  bounds?: { x: number; y: number; w: number; h: number; label?: string }[];
  area: AreaSettings;
  onChange: (fn: (a: AreaSettings) => void) => void;
  unit: string;
}
interface Props {
  display: Frame;
  tablet: Frame;
  tabletConnected: boolean;
  lockAspect?: boolean;
  onLockAspect?: (v: boolean) => void;
}
interface Drag {
  which: Which;
  mode: Mode;
  startX: number;
  startY: number;
  orig: AreaSettings;
}

const VW = 1000;
const PAD = 24;
const GAP = 78;
const HANDLE_PX = 10;

function fit(full: { w: number; h: number }, targetW: number, maxH: number) {
  let scale = targetW / full.w;
  if (full.h * scale > maxH) scale = maxH / full.h;
  return { scale, w: full.w * scale, h: full.h * scale };
}

function corners(a: AreaSettings): Record<ResizeMode, [number, number]> {
  const x0 = a.X - a.Width / 2;
  const y0 = a.Y - a.Height / 2;
  return {
    nw: [x0, y0],
    ne: [x0 + a.Width, y0],
    sw: [x0, y0 + a.Height],
    se: [x0 + a.Width, y0 + a.Height],
  };
}

/** Gaomon-style linked area editor: the display (top) and tablet (bottom) drawn
 *  to scale as real surfaces, with lines projecting the tablet workspace onto
 *  the display region. Drag a rectangle to move, corners to resize (Shift = lock
 *  aspect, Option = from center), right-click to reset it. */
export function MappingEditor({ display, tablet, tabletConnected, lockAspect, onLockAspect }: Props) {
  const svgRef = useRef<SVGSVGElement>(null);
  const drag = useRef<Drag | null>(null);
  const [focused, setFocused] = useState<Which>("tablet");
  const [menu, setMenu] = useState<{ x: number; y: number; which: Which } | null>(null);

  const dFit = fit(display.full, VW - 2 * PAD, 280);
  const tFit = fit(tablet.full, (VW - 2 * PAD) * 0.72, 190);
  const dX0 = (VW - dFit.w) / 2;
  const dY0 = PAD;
  const tY0 = dY0 + dFit.h + GAP;
  const tX0 = (VW - tFit.w) / 2;
  const VH = tY0 + tFit.h + PAD;

  const frame = (w: Which) => (w === "display" ? display : tablet);
  const geo = (w: Which) =>
    w === "display"
      ? { x0: dX0, y0: dY0, scale: dFit.scale }
      : { x0: tX0, y0: tY0, scale: tFit.scale };
  const toSvg = (w: Which, x: number, y: number): [number, number] => {
    const g = geo(w);
    return [g.x0 + x * g.scale, g.y0 + y * g.scale];
  };

  function svgPoint(e: { clientX: number; clientY: number }) {
    const svg = svgRef.current!;
    const pt = svg.createSVGPoint();
    pt.x = e.clientX;
    pt.y = e.clientY;
    const ctm = svg.getScreenCTM();
    return ctm ? pt.matrixTransform(ctm.inverse()) : { x: 0, y: 0 };
  }
  function toLocal(w: Which, e: React.PointerEvent) {
    const g = geo(w);
    const p = svgPoint(e);
    return { x: (p.x - g.x0) / g.scale, y: (p.y - g.y0) / g.scale };
  }

  function start(which: Which, mode: Mode, e: React.PointerEvent) {
    e.stopPropagation();
    setFocused(which);
    svgRef.current?.setPointerCapture(e.pointerId);
    const l = toLocal(which, e);
    drag.current = { which, mode, startX: l.x, startY: l.y, orig: { ...frame(which).area } };
  }

  function move(e: React.PointerEvent) {
    const d = drag.current;
    if (!d) return;
    const l = toLocal(d.which, e);
    const dx = l.x - d.startX;
    const dy = l.y - d.startY;
    const f = frame(d.which);
    f.onChange((a) => {
      if (d.mode === "move") {
        a.X = d.orig.X + dx;
        a.Y = d.orig.Y + dy;
      } else {
        resizeArea(a, d.orig, d.mode, dx, dy, {
          fromCenter: e.altKey,
          keepAspect: e.shiftKey || !!lockAspect,
        });
      }
      clampArea(a, f.full.w, f.full.h);
    });
  }

  function end(e: React.PointerEvent) {
    svgRef.current?.releasePointerCapture(e.pointerId);
    drag.current = null;
  }

  function reset(which: Which) {
    const f = frame(which);
    f.onChange((a) => fullArea(a, f.full.w, f.full.h));
    setMenu(null);
  }

  function openMenu(which: Which, e: React.MouseEvent) {
    e.preventDefault();
    const p = svgPoint(e);
    const box = svgRef.current!.getBoundingClientRect();
    setMenu({ x: (p.x / VW) * box.width, y: (p.y / VH) * box.height, which });
  }

  // Projection frustum: tablet-area corner -> display-area corner.
  const dc = corners(display.area);
  const tc = corners(tablet.area);
  const links = tabletConnected
    ? (["nw", "ne", "se", "sw"] as ResizeMode[]).map((k) => ({
        from: toSvg("tablet", ...tc[k]),
        to: toSvg("display", ...dc[k]),
      }))
    : [];

  const rk = (w: Which) => 8 / geo(w).scale; // surface corner radius in local units

  return (
    <div className="flex flex-col gap-4 lg:flex-row lg:items-start">
      <FieldPanel
        which={focused}
        frame={frame(focused)}
        disabled={focused === "tablet" && !tabletConnected}
        onReset={() => reset(focused)}
        lockAspect={lockAspect}
        onLockAspect={onLockAspect}
      />

      <div className="relative flex-1 rounded-md border bg-muted/20 p-2">
        <svg
          ref={svgRef}
          viewBox={`0 0 ${VW} ${VH}`}
          className="w-full"
          style={{ touchAction: "none" }}
          onPointerMove={move}
          onPointerUp={end}
        >
          {/* --- hardware surfaces --- */}
          {/* Display */}
          <g transform={`translate(${dX0} ${dY0}) scale(${dFit.scale})`}>
            <rect
              x={0}
              y={0}
              width={display.full.w}
              height={display.full.h}
              rx={rk("display")}
              className="fill-muted stroke-border"
              vectorEffect="non-scaling-stroke"
              onContextMenu={(e) => openMenu("display", e)}
            />
            {display.bounds?.map((b, i) => (
              <g key={i}>
                <rect
                  x={b.x}
                  y={b.y}
                  width={b.w}
                  height={b.h}
                  className="fill-muted-foreground/10 stroke-border"
                  vectorEffect="non-scaling-stroke"
                />
                {b.label ? (
                  <text
                    x={b.x + b.w / 2}
                    y={b.y + b.h / 2}
                    className="fill-muted-foreground"
                    style={{ fontSize: Math.min(b.w, b.h) * 0.12 }}
                    textAnchor="middle"
                    dominantBaseline="central"
                  >
                    {b.label}
                  </text>
                ) : null}
              </g>
            ))}
          </g>

          {/* Tablet */}
          <g transform={`translate(${tX0} ${tY0}) scale(${tFit.scale})`}>
            <rect
              x={0}
              y={0}
              width={tablet.full.w}
              height={tablet.full.h}
              rx={rk("tablet")}
              className={tabletConnected ? "fill-muted stroke-border" : "fill-muted/40 stroke-border"}
              vectorEffect="non-scaling-stroke"
              onContextMenu={(e) => tabletConnected && openMenu("tablet", e)}
            />
            {/* express keys, for the tablet look */}
            {Array.from({ length: 4 }).map((_, i) => (
              <rect
                key={i}
                x={tablet.full.w * (0.08 + i * 0.09)}
                y={-tablet.full.h * 0.02}
                width={tablet.full.w * 0.06}
                height={tablet.full.h * 0.04}
                rx={rk("tablet") * 0.4}
                className="fill-muted-foreground/25"
                vectorEffect="non-scaling-stroke"
              />
            ))}
            {!tabletConnected ? (
              <text
                x={tablet.full.w / 2}
                y={tablet.full.h / 2}
                className="fill-muted-foreground"
                style={{ fontSize: tablet.full.h * 0.1 }}
                textAnchor="middle"
                dominantBaseline="central"
              >
                No tablet detected
              </text>
            ) : null}
          </g>

          {/* --- projection lines, above surfaces --- */}
          {links.map((l, i) => (
            <line
              key={i}
              x1={l.from[0]}
              y1={l.from[1]}
              x2={l.to[0]}
              y2={l.to[1]}
              className="stroke-primary/50"
              strokeWidth={1}
              strokeDasharray="4 3"
              vectorEffect="non-scaling-stroke"
            />
          ))}

          {/* --- mapped areas, on top --- */}
          <g transform={`translate(${dX0} ${dY0}) scale(${dFit.scale})`}>
            <AreaShape
              area={display.area}
              focus={focused === "display"}
              handle={HANDLE_PX / dFit.scale}
              onMove={(e) => start("display", "move", e)}
              onResize={(mode, e) => start("display", mode, e)}
            />
          </g>
          {tabletConnected ? (
            <g transform={`translate(${tX0} ${tY0}) scale(${tFit.scale})`}>
              <AreaShape
                area={tablet.area}
                focus={focused === "tablet"}
                handle={HANDLE_PX / tFit.scale}
                onMove={(e) => start("tablet", "move", e)}
                onResize={(mode, e) => start("tablet", mode, e)}
              />
            </g>
          ) : null}
        </svg>

        {menu ? (
          <>
            <div
              className="fixed inset-0 z-40"
              onClick={() => setMenu(null)}
              onContextMenu={(e) => {
                e.preventDefault();
                setMenu(null);
              }}
            />
            <div
              className="absolute z-50 min-w-44 rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
              style={{ left: menu.x, top: menu.y }}
            >
              <button
                className="w-full rounded-sm px-2 py-1.5 text-left text-sm capitalize hover:bg-accent hover:text-accent-foreground"
                onClick={() => reset(menu.which)}
              >
                Reset {menu.which} to full
              </button>
            </div>
          </>
        ) : null}
      </div>
    </div>
  );
}

function AreaShape({
  area,
  focus,
  handle,
  onMove,
  onResize,
}: {
  area: AreaSettings;
  focus: boolean;
  handle: number;
  onMove: (e: React.PointerEvent) => void;
  onResize: (mode: ResizeMode, e: React.PointerEvent) => void;
}) {
  const x = area.X - area.Width / 2;
  const y = area.Y - area.Height / 2;
  const c = corners(area);
  const cursors: Record<ResizeMode, string> = {
    nw: "nwse-resize",
    ne: "nesw-resize",
    sw: "nesw-resize",
    se: "nwse-resize",
  };
  return (
    <g transform={`rotate(${area.Rotation} ${area.X} ${area.Y})`}>
      <rect
        x={x}
        y={y}
        width={area.Width}
        height={area.Height}
        className={focus ? "fill-primary/35 stroke-primary" : "fill-primary/15 stroke-primary/70"}
        strokeWidth={focus ? 2 : 1.5}
        style={{ cursor: "move" }}
        vectorEffect="non-scaling-stroke"
        onPointerDown={onMove}
      />
      {(Object.keys(c) as ResizeMode[]).map((mode) => (
        <rect
          key={mode}
          x={c[mode][0] - handle / 2}
          y={c[mode][1] - handle / 2}
          width={handle}
          height={handle}
          className="fill-primary stroke-background"
          strokeWidth={1}
          style={{ cursor: cursors[mode] }}
          vectorEffect="non-scaling-stroke"
          onPointerDown={(e) => onResize(mode, e)}
        />
      ))}
    </g>
  );
}

function FieldPanel({
  which,
  frame,
  disabled,
  onReset,
  lockAspect,
  onLockAspect,
}: {
  which: Which;
  frame: Frame;
  disabled: boolean;
  onReset: () => void;
  lockAspect?: boolean;
  onLockAspect?: (v: boolean) => void;
}) {
  const a = frame.area;
  const set = (fn: (a: AreaSettings) => void) =>
    frame.onChange((d) => {
      fn(d);
      clampArea(d, frame.full.w, frame.full.h);
    });
  const round = (v: number) => (Number.isFinite(v) ? Math.round(v * 100) / 100 : 0);
  return (
    <div className="w-full shrink-0 space-y-2 lg:w-52">
      <div className="text-sm font-semibold capitalize">{which} area</div>
      {disabled ? (
        <p className="text-xs text-muted-foreground">Connect a tablet to configure its area.</p>
      ) : (
        <>
          <Field label={`Width (${frame.unit})`} value={round(a.Width)} onChange={(v) => set((d) => void (d.Width = v))} />
          <Field label={`Height (${frame.unit})`} value={round(a.Height)} onChange={(v) => set((d) => void (d.Height = v))} />
          <Field label={`X (${frame.unit})`} value={round(a.X)} onChange={(v) => set((d) => void (d.X = v))} />
          <Field label={`Y (${frame.unit})`} value={round(a.Y)} onChange={(v) => set((d) => void (d.Y = v))} />
          <Field label="Rotation °" value={round(a.Rotation)} onChange={(v) => set((d) => void (d.Rotation = v))} />
          <label className="flex items-center gap-2 pt-1 text-xs text-muted-foreground">
            <input
              type="checkbox"
              checked={!!lockAspect}
              onChange={(e) => onLockAspect?.(e.target.checked)}
            />
            Lock aspect ratio
          </label>
          <Button variant="outline" size="sm" className="w-full" onClick={onReset}>
            <Maximize2 className="mr-1 size-3.5" /> Fill {which}
          </Button>
        </>
      )}
      <p className="text-[11px] leading-snug text-muted-foreground">
        Drag to move, corners to resize. Shift = lock aspect, Option = from center, right-click to
        reset.
      </p>
    </div>
  );
}

function Field({
  label,
  value,
  onChange,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
}) {
  return (
    <div className="flex items-center gap-2">
      <Label className="w-24 shrink-0 text-right text-xs text-muted-foreground">{label}</Label>
      <Input type="number" className="h-8" value={value} onChange={(e) => onChange(Number(e.target.value))} />
    </div>
  );
}
