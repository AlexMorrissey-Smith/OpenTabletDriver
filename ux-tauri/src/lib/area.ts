import type { AreaSettings } from "./types";

export type ResizeMode = "nw" | "ne" | "sw" | "se";

export const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

/** Keep the (possibly rotated) area inside the frame. X/Y is the center.
 *  ponytail: W/H cap is the frame extent, not the exact rotated fit — a rotated
 *  corner can still touch the edge; upgrade to solving the rotated AABB if needed. */
export function clampArea(a: AreaSettings, fw: number, fh: number) {
  a.Width = clamp(a.Width, 1, fw);
  a.Height = clamp(a.Height, 1, fh);
  const rad = (a.Rotation * Math.PI) / 180;
  const c = Math.abs(Math.cos(rad));
  const s = Math.abs(Math.sin(rad));
  const hw = (c * a.Width + s * a.Height) / 2;
  const hh = (s * a.Width + c * a.Height) / 2;
  a.X = clamp(a.X, Math.min(hw, fw / 2), Math.max(fw - hw, fw / 2));
  a.Y = clamp(a.Y, Math.min(hh, fh / 2), Math.max(fh - hh, fh / 2));
}

/** Corner resize with Photoshop modifiers, computed in unrotated axes.
 *  Default anchors the opposite corner; fromCenter (Option) grows about the
 *  center; keepAspect (Shift / lock setting) preserves the original ratio. */
export function resizeArea(
  a: AreaSettings,
  orig: AreaSettings,
  mode: ResizeMode,
  dx: number,
  dy: number,
  opts: { fromCenter: boolean; keepAspect: boolean },
) {
  const sx = mode.includes("e") ? 1 : -1;
  const sy = mode.includes("s") ? 1 : -1;
  let w = Math.max(1, orig.Width + (opts.fromCenter ? 2 : 1) * dx * sx);
  let h = Math.max(1, orig.Height + (opts.fromCenter ? 2 : 1) * dy * sy);
  if (opts.keepAspect) {
    const aspect = orig.Width / orig.Height;
    if (Math.abs(w / orig.Width - 1) >= Math.abs(h / orig.Height - 1)) h = w / aspect;
    else w = h * aspect;
  }
  a.Width = w;
  a.Height = h;
  a.X = opts.fromCenter ? orig.X : orig.X + (sx * (w - orig.Width)) / 2;
  a.Y = opts.fromCenter ? orig.Y : orig.Y + (sy * (h - orig.Height)) / 2;
}

/** Reset an area to fill its frame. */
export function fullArea(a: AreaSettings, fw: number, fh: number) {
  a.X = fw / 2;
  a.Y = fh / 2;
  a.Width = fw;
  a.Height = fh;
  a.Rotation = 0;
}
