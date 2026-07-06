import { useCallback, useEffect, useState } from "react";
import { daemon, events } from "@/lib/daemon";
import { currentProfile, useStore } from "@/lib/store";
import { findType, makeStore } from "@/lib/plugin";
import type { AreaSettings, VirtualScreenInfo } from "@/lib/types";
import { MappingEditor } from "../MappingEditor";
import { Section } from "../Section";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

function isAbsolutePath(path: string): boolean {
  return path.includes("Absolute") || path.includes("Artist");
}

export function OutputPage() {
  const profile = useStore(currentProfile);
  const catalog = useStore((s) => s.catalog);
  const tablets = useStore((s) => s.tablets);
  const selectedTablet = useStore((s) => s.selectedTablet);
  const updateProfile = useStore((s) => s.updateProfile);

  // Display geometry comes from the DAEMON's virtual screen (driver coordinate
  // space), not the webview's monitor list — only this matches how the driver
  // maps the tablet, and it carries each monitor's rect for multi-display layouts.
  // Display config can change at runtime (plug/unplug, mirror, resolution), so
  // refetch on daemon Resynchronize (fires on virtual-screen change) and on
  // window focus — GetVirtualScreen re-reads the live screen each call.
  const [vscreen, setVscreen] = useState<VirtualScreenInfo | null>(null);
  const refresh = useCallback(() => {
    daemon.getVirtualScreen().then(setVscreen).catch(() => {});
  }, []);
  useEffect(() => {
    refresh();
    const un = events.onResynchronize(refresh);
    window.addEventListener("focus", refresh);
    return () => {
      un.then((f) => f());
      window.removeEventListener("focus", refresh);
    };
  }, [refresh]);

  const displayFull = { w: vscreen?.Width || 1920, h: vscreen?.Height || 1080 };
  // Normalize monitor rects into the area's [0..Width]×[0..Height] space (matches
  // how the daemon stores Display.X/Y, which are offset from the min corner).
  const minX = vscreen ? Math.min(...vscreen.Displays.map((d) => d.X), vscreen.X) : 0;
  const minY = vscreen ? Math.min(...vscreen.Displays.map((d) => d.Y), vscreen.Y) : 0;
  const displayBounds = vscreen?.Displays.map((d, i) => ({
    x: d.X - minX,
    y: d.Y - minY,
    w: d.Width,
    h: d.Height,
    label: `${i + 1}`,
  }));

  if (!profile) return null;

  const path = profile.OutputMode?.Path ?? "";
  const absolute = isAbsolutePath(path);

  // Physical tablet size (mm) from specs, for the tablet-area background.
  // `tablets` is the live-detected list, so its presence = actually connected.
  const tablet = tablets.find((t) => t.Properties?.Name === selectedTablet);
  const tabletConnected = !!tablet;
  const digitizer = (tablet?.Properties as any)?.Specifications?.Digitizer;
  const tabletFull = {
    w: Number(digitizer?.Width) || profile.AbsoluteModeSettings?.Tablet?.Width || 100,
    h: Number(digitizer?.Height) || profile.AbsoluteModeSettings?.Tablet?.Height || 100,
  };

  const abs = profile.AbsoluteModeSettings;
  const rel = profile.RelativeModeSettings;

  const editDisplay = (fn: (a: AreaSettings) => void) =>
    updateProfile((p) => {
      if (p.AbsoluteModeSettings?.Display) fn(p.AbsoluteModeSettings.Display);
    });
  const editTablet = (fn: (a: AreaSettings) => void) =>
    updateProfile((p) => {
      if (p.AbsoluteModeSettings?.Tablet) fn(p.AbsoluteModeSettings.Tablet);
    });

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <Section title="Output mode">
        {catalog ? (
          <Select
            value={path}
            onValueChange={(p) => {
              const type = findType(catalog, "OutputModes", p);
              if (type) updateProfile((prof) => void (prof.OutputMode = makeStore(type)));
            }}
          >
            <SelectTrigger className="w-72">
              <SelectValue placeholder="Output mode">
                {(v: string) =>
                  catalog.OutputModes.find((t) => t.Path === v)?.Name ?? v.split(".").pop()
                }
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              {catalog.OutputModes.map((t) => (
                <SelectItem key={t.Path} value={t.Path}>
                  {t.Name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : (
          <p className="text-sm text-muted-foreground">{path.split(".").pop()}</p>
        )}
      </Section>

      {absolute && abs ? (
        <Section
          title="Area mapping"
          description="The tablet workspace (bottom) maps onto the display region (top)."
        >
          {abs.Display && abs.Tablet ? (
            <MappingEditor
              display={{ full: displayFull, bounds: displayBounds, area: abs.Display, onChange: editDisplay, unit: "px" }}
              tablet={{ full: tabletFull, area: abs.Tablet, onChange: editTablet, unit: "mm" }}
              tabletConnected={tabletConnected}
              lockAspect={!!abs.LockAspectRatio}
              onLockAspect={(v) => updateProfile((p) => void (p.AbsoluteModeSettings!.LockAspectRatio = v))}
            />
          ) : null}
        </Section>
      ) : rel ? (
        <Section title="Relative mode">
          <div className="grid gap-4 sm:grid-cols-2">
            <NumField
              label="X sensitivity (px/mm)"
              value={rel.XSensitivity}
              onChange={(v) => updateProfile((p) => void (p.RelativeModeSettings!.XSensitivity = v))}
            />
            <NumField
              label="Y sensitivity (px/mm)"
              value={rel.YSensitivity}
              onChange={(v) => updateProfile((p) => void (p.RelativeModeSettings!.YSensitivity = v))}
            />
            <NumField
              label="Rotation °"
              value={rel.RelativeRotation}
              onChange={(v) => updateProfile((p) => void (p.RelativeModeSettings!.RelativeRotation = v))}
            />
            <div className="space-y-1">
              <Label className="text-xs text-muted-foreground">Reset delay</Label>
              <Input
                value={rel.RelativeResetDelay}
                onChange={(e) =>
                  updateProfile((p) => void (p.RelativeModeSettings!.RelativeResetDelay = e.target.value))
                }
              />
            </div>
          </div>
        </Section>
      ) : null}
    </div>
  );
}

function NumField({ label, value, onChange }: { label: string; value: number; onChange: (v: number) => void }) {
  return (
    <div className="space-y-1">
      <Label className="text-xs text-muted-foreground">{label}</Label>
      <Input type="number" value={value} onChange={(e) => onChange(Number(e.target.value))} />
    </div>
  );
}
