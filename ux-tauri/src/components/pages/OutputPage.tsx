import { useCallback, useEffect, useState } from "react";
import { daemon, events } from "@/lib/daemon";
import { currentProfile, useStore } from "@/lib/store";
import { findType, makeStore } from "@/lib/plugin";
import type { AreaSettings, VirtualScreenInfo } from "@/lib/types";
import { AreaEditor } from "../AreaEditor";
import { Section } from "../Section";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
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
  const tablet = tablets.find((t) => t.Properties?.Name === selectedTablet);
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
        <>
          <Section title="Display area" description="The region of your screen the tablet maps to.">
            {abs.Display ? (
              <AreaEditor
                area={abs.Display}
                fullWidth={displayFull.w}
                fullHeight={displayFull.h}
                bounds={displayBounds}
                unit="px"
                lockAspect={!!abs.LockAspectRatio}
                onChange={editDisplay}
              />
            ) : null}
          </Section>

          <Section title="Tablet area" description="The active region of the tablet surface.">
            <div className="space-y-4">
              {abs.Tablet ? (
                <AreaEditor
                  area={abs.Tablet}
                  fullWidth={tabletFull.w}
                  fullHeight={tabletFull.h}
                  unit="mm"
                  lockAspect={!!abs.LockAspectRatio}
                  onChange={editTablet}
                />
              ) : null}
              <div className="space-y-3">
                <Toggle
                  label="Lock aspect ratio"
                  checked={!!abs.LockAspectRatio}
                  onChange={(v) => updateProfile((p) => void (p.AbsoluteModeSettings!.LockAspectRatio = v))}
                />
                <Toggle
                  label="Area clipping"
                  checked={!!abs.EnableClipping}
                  onChange={(v) => updateProfile((p) => void (p.AbsoluteModeSettings!.EnableClipping = v))}
                />
                <Toggle
                  label="Ignore reports outside area"
                  checked={!!abs.EnableAreaLimiting}
                  onChange={(v) => updateProfile((p) => void (p.AbsoluteModeSettings!.EnableAreaLimiting = v))}
                />
              </div>
            </div>
          </Section>
        </>
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

function Toggle({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="flex items-center justify-between">
      <Label className="text-sm">{label}</Label>
      <Switch checked={checked} onCheckedChange={onChange} />
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
