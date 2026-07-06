// HUD rendered in the transparent "overlay" window (see src-tauri/src/overlay.rs).
// Payload arrives urlencoded in the location hash: #overlay=<json OverlayRequest>.
// macOS never routes here — its HUDs are native Swift helpers.

interface OverlayRequest {
  Kind: string;
  Payload: unknown;
}

interface SwapDisplay {
  X: number;
  Y: number;
  W: number;
  H: number;
}

export function parseOverlayHash(hash: string): OverlayRequest | null {
  if (!hash.startsWith("#overlay=")) return null;
  try {
    return JSON.parse(decodeURIComponent(hash.slice("#overlay=".length)));
  } catch {
    return null;
  }
}

export function OverlayHud({ request }: { request: OverlayRequest }) {
  return (
    <div className="pointer-events-none fixed inset-0 flex items-start justify-center bg-transparent">
      {request.Kind === "wheel-mode" ? (
        <WheelModePill mode={String(request.Payload ?? "")} />
      ) : request.Kind === "display-swap" ? (
        <DisplaySwapDiagram payload={request.Payload as { Selection: string; Displays: SwapDisplay[] }} />
      ) : null}
    </div>
  );
}

function WheelModePill({ mode }: { mode: string }) {
  return (
    <div className="mt-24 rounded-2xl bg-black/70 px-10 py-5 text-center shadow-2xl backdrop-blur">
      <div className="text-xs uppercase tracking-widest text-white/60">Wheel mode</div>
      <div className="mt-1 text-2xl font-semibold text-white">{mode}</div>
    </div>
  );
}

function DisplaySwapDiagram({ payload }: { payload: { Selection: string; Displays: SwapDisplay[] } }) {
  const displays = payload?.Displays ?? [];
  if (displays.length === 0) return null;

  const minX = Math.min(...displays.map((d) => d.X));
  const minY = Math.min(...displays.map((d) => d.Y));
  const maxX = Math.max(...displays.map((d) => d.X + d.W));
  const maxY = Math.max(...displays.map((d) => d.Y + d.H));
  const vw = maxX - minX;
  const vh = maxY - minY;
  const all = payload.Selection === "all";
  const selected = all ? -1 : Number(payload.Selection);

  return (
    <div className="mt-24 rounded-2xl bg-black/70 p-6 shadow-2xl backdrop-blur">
      <div className="mb-3 text-center text-xs uppercase tracking-widest text-white/60">
        Output → {all ? "All displays" : `Display ${selected + 1}`}
      </div>
      <svg width={320} height={(320 * vh) / vw} viewBox={`0 0 ${vw} ${vh}`}>
        {displays.map((d, i) => {
          const active = all || i === selected;
          return (
            <g key={i}>
              <rect
                x={d.X - minX}
                y={d.Y - minY}
                width={d.W}
                height={d.H}
                rx={vw * 0.008}
                fill={active ? "rgba(96,165,250,0.85)" : "rgba(255,255,255,0.15)"}
                stroke="rgba(255,255,255,0.5)"
                strokeWidth={vw * 0.003}
              />
              <text
                x={d.X - minX + d.W / 2}
                y={d.Y - minY + d.H / 2}
                textAnchor="middle"
                dominantBaseline="central"
                fill={active ? "#0b1220" : "rgba(255,255,255,0.7)"}
                fontSize={Math.min(d.W, d.H) * 0.3}
                fontWeight={600}
              >
                {i + 1}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
