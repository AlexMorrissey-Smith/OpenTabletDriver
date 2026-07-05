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
    { id: "tablet", label: "Tablet", icon: Tablet, visible: hasTablet },
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
    </nav>
  );
}
