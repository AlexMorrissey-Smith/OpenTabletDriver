import { useStore } from "@/lib/store";
import { daemon } from "@/lib/daemon";
import { Sidebar } from "./Sidebar";
import { Header } from "./Header";
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

const PAGES: Record<string, () => React.ReactElement | null> = {
  tablet: OutputPage,
  pen: PenPage,
  aux: AuxPage,
  mouse: MousePage,
  wheel: WheelPage,
  tools: ToolsPage,
  filters: FiltersPage,
  console: ConsolePage,
};

export function AppShell() {
  const connected = useStore((s) => s.connected);
  const activePage = useStore((s) => s.activePage);
  const Page = PAGES[activePage] ?? OutputPage;

  return (
    <div className="flex h-screen flex-col bg-background text-foreground">
      <Header>
        <ThemeToggle />
        <AppMenu />
      </Header>

      <div className="flex min-h-0 flex-1">
        <Sidebar />
        <main className="flex-1 overflow-auto p-4">
          {connected ? (
            <Page />
          ) : (
            <div className="flex h-full flex-col items-center justify-center gap-3 text-center">
              <p className="text-muted-foreground">Connecting to the OpenTabletDriver daemon…</p>
              <button
                className="text-sm underline"
                onClick={() => daemon.detectTablets().catch(() => {})}
              >
                Retry
              </button>
            </div>
          )}
        </main>
      </div>
    </div>
  );
}
