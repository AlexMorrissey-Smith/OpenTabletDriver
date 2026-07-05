import { useRef, useState } from "react";
import type { AreaSettings } from "@/lib/types";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

type Mode = "move" | "nw" | "ne" | "sw" | "se";
interface Drag {
  mode: Mode;
  startX: number;
  startY: number;
  orig: AreaSettings;
}

interface Props {
  area: AreaSettings;
  fullWidth: number;
  fullHeight: number;
  unit: string;
  /** Optional sub-rectangles to draw inside the frame (e.g. individual monitors),
   *  in the same coordinate space as the area. */
  bounds?: { x: number; y: number; w: number; h: number; label?: string }[];
  /** Force aspect-lock during resize (the "Lock aspect ratio" setting); Shift does
   *  the same on the fly. */
  lockAspect?: boolean;
  onChange: (fn: (a: AreaSettings) => void) => void;
}

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

/** Keep the (possibly rotated) area inside the frame. X/Y is the center.
 *  Caps W/H to the frame, then clamps the center so the rotated bounding box
 *  stays within [0,fw]×[0,fh]. When the box is larger than the frame the
 *  clamp collapses to the frame center (best it can do without shrinking).
 *  ponytail: W/H cap is the frame extent, not the exact rotated fit — a
 *  rotated corner can still touch the edge; upgrade to solving the rotated
 *  AABB for W/H if that matters. */
function clampArea(a: AreaSettings, fw: number, fh: number) {
  a.Width = clamp(a.Width, 1, fw);
  a.Height = clamp(a.Height, 1, fh);
  const rad = (a.Rotation * Math.PI) / 180;
  const c = Math.abs(Math.cos(rad));
  const s = Math.abs(Math.sin(rad));
  const hw = (c * a.Width + s * a.Height) / 2; // rotated AABB half-width
  const hh = (s * a.Width + c * a.Height) / 2;
  a.X = clamp(a.X, Math.min(hw, fw / 2), Math.max(fw - hw, fw / 2));
  a.Y = clamp(a.Y, Math.min(hh, fh / 2), Math.max(fh - hh, fh / 2));
}

/** Visual area editor. X/Y are the area CENTER (matches the daemon's model). Drag
 *  body to move, corners to resize. Resize modifiers mirror Photoshop:
 *  default anchors the opposite corner, Option/Alt resizes about the center,
 *  Shift (or the Lock-aspect setting) keeps the aspect ratio. Right-click resets
 *  the area to fill the frame.
 *  ponytail: move/resize computed in unrotated axes — slight skew while rotated;
 *  the precise-values panel gives exact control. Upgrade to rotated-frame math if
 *  users need it. */
export function AreaEditor({ area, fullWidth, fullHeight, unit, bounds, lockAspect, onChange }: Props) {
  const svgRef = useRef<SVGSVGElement>(null);
  const drag = useRef<Drag | null>(null);
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);
  const handle = Math.max(fullWidth, fullHeight) * 0.02;

  // Every mutation is clamped back inside the frame.
  const edit = (fn: (a: AreaSettings) => void) =>
    onChange((a) => {
      fn(a);
      clampArea(a, fullWidth, fullHeight);
    });

  function toSvg(e: React.PointerEvent) {
    const svg = svgRef.current;
    if (!svg) return { x: 0, y: 0 };
    const pt = svg.createSVGPoint();
    pt.x = e.clientX;
    pt.y = e.clientY;
    const ctm = svg.getScreenCTM();
    return ctm ? pt.matrixTransform(ctm.inverse()) : { x: 0, y: 0 };
  }

  function start(mode: Mode, e: React.PointerEvent) {
    e.stopPropagation();
    svgRef.current?.setPointerCapture(e.pointerId);
    const p = toSvg(e);
    drag.current = { mode, startX: p.x, startY: p.y, orig: { ...area } };
  }

  function move(e: React.PointerEvent) {
    if (!drag.current) return;
    const p = toSvg(e);
    const dx = p.x - drag.current.startX;
    const dy = p.y - drag.current.startY;
    const o = drag.current.orig;
    const mode = drag.current.mode;
    edit((a) => {
      if (mode === "move") {
        a.X = o.X + dx;
        a.Y = o.Y + dy;
        return;
      }
      const sx = mode.includes("e") ? 1 : -1; // which x-edge the corner drives
      const sy = mode.includes("s") ? 1 : -1;
      const fromCenter = e.altKey; // Option: symmetric about center
      const keepAspect = e.shiftKey || !!lockAspect;

      // Default: opposite corner fixed → one edge moves by the drag. Option: both
      // opposite edges move → grows about the center (×2).
      let w = Math.max(1, o.Width + (fromCenter ? 2 : 1) * dx * sx);
      let h = Math.max(1, o.Height + (fromCenter ? 2 : 1) * dy * sy);
      if (keepAspect) {
        const aspect = o.Width / o.Height;
        // Follow whichever axis the cursor pushed harder, scale the other to match.
        if (Math.abs(w / o.Width - 1) >= Math.abs(h / o.Height - 1)) h = w / aspect;
        else w = h * aspect;
      }
      a.Width = w;
      a.Height = h;
      // Anchor: center stays put with Option; otherwise the fixed corner does, so
      // the center shifts by half the size change in the drag direction.
      a.X = fromCenter ? o.X : o.X + (sx * (w - o.Width)) / 2;
      a.Y = fromCenter ? o.Y : o.Y + (sy * (h - o.Height)) / 2;
    });
  }

  function end(e: React.PointerEvent) {
    svgRef.current?.releasePointerCapture(e.pointerId);
    drag.current = null;
  }

  function resetToFull() {
    edit((a) => {
      a.X = fullWidth / 2;
      a.Y = fullHeight / 2;
      a.Width = fullWidth;
      a.Height = fullHeight;
      a.Rotation = 0;
    });
    setMenu(null);
  }

  const x = area.X - area.Width / 2;
  const y = area.Y - area.Height / 2;
  const corners: [Mode, number, number, string][] = [
    ["nw", x, y, "nwse-resize"],
    ["ne", x + area.Width, y, "nesw-resize"],
    ["sw", x, y + area.Height, "nesw-resize"],
    ["se", x + area.Width, y + area.Height, "nwse-resize"],
  ];

  return (
    <div className="space-y-3">
      <div
        className="relative rounded-md border bg-muted/30 p-2"
        onContextMenu={(e) => {
          e.preventDefault();
          const r = e.currentTarget.getBoundingClientRect();
          setMenu({ x: e.clientX - r.left, y: e.clientY - r.top });
        }}
      >
        <svg
          ref={svgRef}
          viewBox={`0 0 ${fullWidth} ${fullHeight}`}
          className="w-full"
          style={{ maxHeight: 320, touchAction: "none" }}
          preserveAspectRatio="xMidYMid meet"
          onPointerMove={move}
          onPointerUp={end}
        >
          <rect
            x={0}
            y={0}
            width={fullWidth}
            height={fullHeight}
            className="fill-background stroke-border"
            vectorEffect="non-scaling-stroke"
          />
          {/* Individual monitors, so multi-display layouts read correctly. */}
          {bounds?.map((b, i) => (
            <g key={i}>
              <rect
                x={b.x}
                y={b.y}
                width={b.w}
                height={b.h}
                className="fill-muted-foreground/10 stroke-border"
                strokeWidth={1}
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
          <g transform={`rotate(${area.Rotation} ${area.X} ${area.Y})`}>
            <rect
              x={x}
              y={y}
              width={area.Width}
              height={area.Height}
              className="fill-primary/20 stroke-primary cursor-move"
              strokeWidth={2}
              vectorEffect="non-scaling-stroke"
              onPointerDown={(e) => start("move", e)}
            />
            {corners.map(([mode, cx, cy, cursor]) => (
              <rect
                key={mode}
                x={cx - handle / 2}
                y={cy - handle / 2}
                width={handle}
                height={handle}
                className="fill-primary"
                style={{ cursor }}
                onPointerDown={(e) => start(mode, e)}
              />
            ))}
          </g>
        </svg>

        {menu ? (
          <>
            {/* click-away catcher */}
            <div className="fixed inset-0 z-40" onClick={() => setMenu(null)} onContextMenu={(e) => { e.preventDefault(); setMenu(null); }} />
            <div
              className="absolute z-50 min-w-40 rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
              style={{ left: menu.x, top: menu.y }}
            >
              <button
                className="w-full rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                onClick={resetToFull}
              >
                Reset to full area
              </button>
            </div>
          </>
        ) : null}
      </div>

      <details className="group">
        <summary className="cursor-pointer select-none text-xs text-muted-foreground hover:text-foreground">
          Precise values
        </summary>
        <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-5">
          <NumField label={`W (${unit})`} value={area.Width} onChange={(v) => edit((a) => void (a.Width = v))} />
          <NumField label={`H (${unit})`} value={area.Height} onChange={(v) => edit((a) => void (a.Height = v))} />
          <NumField label={`X (${unit})`} value={area.X} onChange={(v) => edit((a) => void (a.X = v))} />
          <NumField label={`Y (${unit})`} value={area.Y} onChange={(v) => edit((a) => void (a.Y = v))} />
          <NumField label="Rotation °" value={area.Rotation} onChange={(v) => edit((a) => void (a.Rotation = v))} />
        </div>
      </details>
    </div>
  );
}

function NumField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
}) {
  return (
    <div className="space-y-1">
      <Label className="text-xs text-muted-foreground">{label}</Label>
      <Input
        type="number"
        value={Number.isFinite(value) ? Math.round(value * 100) / 100 : 0}
        onChange={(e) => onChange(Number(e.target.value))}
      />
    </div>
  );
}
