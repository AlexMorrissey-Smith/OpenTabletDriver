import { currentBindings, useStore } from "@/lib/store";
import { cn } from "@/lib/utils";
import {
  Disc3,
  Filter,
  Grid2x2,
  Mouse,
  PenTool,
  Tablet,
  Terminal,
  Wrench,
  type LucideIcon,
} from "lucide-react";

interface NavItem {
  id: string;
  label: string;
  icon: LucideIcon;
  visible: boolean;
}

export function Sidebar() {
  const bindings = useStore(currentBindings);
  const activePage = useStore((s) => s.activePage);
  const setPage = useStore((s) => s.setPage);

  const hasAux = (bindings?.AuxButtons?.length ?? 0) > 0;
  const hasMouse = (bindings?.MouseButtons?.length ?? 0) > 0;
  const hasWheel = (bindings?.WheelBindings?.length ?? 0) > 0;
  const hasTablet = !!bindings;

  const items: NavItem[] = [
    // Tablet stays visible with no tablet: it hosts the detect empty state.
    { id: "tablet", label: "Tablet", icon: Tablet, visible: true },
    { id: "pen", label: "Pen", icon: PenTool, visible: hasTablet },
    { id: "aux", label: "Buttons", icon: Grid2x2, visible: hasAux },
    { id: "mouse", label: "Mouse", icon: Mouse, visible: hasMouse },
    { id: "wheel", label: "Wheel", icon: Disc3, visible: hasWheel },
    { id: "tools", label: "Tools", icon: Wrench, visible: true },
    { id: "filters", label: "Filters", icon: Filter, visible: true },
    { id: "console", label: "Console", icon: Terminal, visible: true },
  ];

  return (
    <nav className="flex w-48 flex-col gap-1 border-r bg-sidebar p-2">
      {items
        .filter((i) => i.visible)
        .map((item) => {
          const Icon = item.icon;
          const active = activePage === item.id;
          return (
            <button
              key={item.id}
              onClick={() => setPage(item.id)}
              className={cn(
                "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
                active
                  ? "bg-sidebar-accent text-sidebar-accent-foreground font-medium"
                  : "text-sidebar-foreground/70 hover:bg-sidebar-accent/50",
              )}
            >
              <Icon className="size-4" />
              {item.label}
            </button>
          );
        })}

      <StatusFooter />
    </nav>
  );
}

/** Always-visible answer to "is it working?": daemon link + detected tablet. */
function StatusFooter() {
  const connected = useStore((s) => s.connected);
  const tablets = useStore((s) => s.tablets);

  const tabletName = tablets[0]?.Properties?.Name;

  return (
    <div className="mt-auto select-none border-t border-border/60 px-3 pt-3 pb-1 text-xs">
      <div className="flex items-center gap-2">
        <span
          className={cn(
            "size-2 shrink-0 rounded-full",
            connected ? "bg-emerald-500" : "bg-red-500",
          )}
        />
        <span className="text-sidebar-foreground/70">
          {connected ? "Driver running" : "Driver offline"}
        </span>
      </div>
      <div className="mt-1.5 flex items-center gap-2">
        <span
          className={cn(
            "size-2 shrink-0 rounded-full",
            tabletName ? "bg-emerald-500" : "bg-muted-foreground/40",
          )}
        />
        <span className="truncate text-sidebar-foreground/70" title={tabletName}>
          {tabletName ?? "No tablet"}
        </span>
      </div>
    </div>
  );
}
