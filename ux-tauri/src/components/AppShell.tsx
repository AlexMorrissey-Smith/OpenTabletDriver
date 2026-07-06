import { currentProfile, useStore } from "@/lib/store";
import { Sidebar } from "./Sidebar";
import { Header } from "./Header";
import { NoTabletState } from "./EmptyState";
import { OutputPage } from "./pages/OutputPage";
import { PenPage } from "./pages/PenPage";
import { AuxPage } from "./pages/AuxPage";
import { MousePage } from "./pages/MousePage";
import { WheelPage } from "./pages/WheelPage";
import { ToolsPage } from "./pages/ToolsPage";
import { FiltersPage } from "./pages/FiltersPage";
import { ConsolePage } from "./pages/ConsolePage";
import { AppMenu } from "./AppMenu";
import { ThemeToggle } from "./ThemeToggle";
import { Loader2 } from "lucide-react";

interface PageDef {
  component: () => React.ReactElement | null;
  title: string;
  description: string;
  /** Full-width content (log tables); others center in a reading column. */
  wide?: boolean;
  /** Usable without a configured tablet. */
  standalone?: boolean;
}

const PAGES: Record<string, PageDef> = {
  tablet: {
    component: OutputPage,
    title: "Tablet",
    description: "How pen movement maps onto your displays.",
  },
  pen: {
    component: PenPage,
    title: "Pen",
    description: "Tip, eraser and pen button bindings.",
  },
  aux: {
    component: AuxPage,
    title: "Buttons",
    description: "Bindings for the tablet's express keys.",
  },
  mouse: {
    component: MousePage,
    title: "Mouse",
    description: "Bindings for the tablet mouse buttons.",
  },
  wheel: {
    component: WheelPage,
    title: "Wheel",
    description: "Ring and dial behavior, per wheel mode.",
  },
  tools: {
    component: ToolsPage,
    title: "Tools",
    description: "Utilities that run alongside the driver.",
  },
  filters: {
    component: FiltersPage,
    title: "Filters",
    description: "Smoothing and input processing, applied in order.",
  },
  console: {
    component: ConsolePage,
    title: "Console",
    description: "Live driver log.",
    wide: true,
    standalone: true,
  },
};

export function AppShell() {
  const connected = useStore((s) => s.connected);
  const activePage = useStore((s) => s.activePage);
  const profile = useStore(currentProfile);

  const page = PAGES[activePage] ?? PAGES.tablet;
  const Page = page.component;
  const column = page.wide ? "w-full" : "mx-auto w-full max-w-3xl";

  return (
    <div className="flex h-screen flex-col bg-background text-foreground">
      <Header>
        <ThemeToggle />
        <AppMenu />
      </Header>

      <div className="flex min-h-0 flex-1">
        <Sidebar />
        <main className="flex min-h-0 flex-1 flex-col">
          {!connected ? (
            <Connecting />
          ) : !profile && !page.standalone ? (
            <NoTabletState />
          ) : (
            <>
              <div className={`${column} shrink-0 select-none px-6 pt-5 pb-4`}>
                <h1 className="text-lg font-semibold">{page.title}</h1>
                <p className="text-sm text-muted-foreground">{page.description}</p>
              </div>
              <div className="min-h-0 flex-1 overflow-auto px-6 pb-6">
                <div className={`${column} h-full`}>
                  <Page />
                </div>
              </div>
            </>
          )}
        </main>
      </div>
    </div>
  );
}

function Connecting() {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 text-center select-none">
      <Loader2 className="size-5 animate-spin text-muted-foreground" />
      <p className="text-sm text-muted-foreground">
        Connecting to the driver…
        <br />
        The daemon starts automatically and reconnects on its own.
      </p>
    </div>
  );
}
