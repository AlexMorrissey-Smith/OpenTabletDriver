import { useCallback, useEffect, useState } from "react";
import { open } from "@tauri-apps/plugin-dialog";
import { daemon } from "@/lib/daemon";
import { useStore } from "@/lib/store";
import { useDialog } from "@/lib/ui";
import type { PluginMetadata } from "@/lib/types";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";

export function PluginManagerDialog() {
  const d = useDialog("plugins");
  const refreshCatalog = useStore((s) => s.refreshCatalog);
  const [installed, setInstalled] = useState<PluginMetadata[]>([]);
  const [repo, setRepo] = useState<PluginMetadata[]>([]);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setInstalled(await daemon.getLoadedPlugins().catch(() => []));
    setRepo(await daemon.getPluginMetadataRepository().catch(() => []));
  }, []);

  useEffect(() => {
    if (d.open) load();
  }, [d.open, load]);

  async function afterChange() {
    await daemon.loadPlugins().catch(() => {});
    await refreshCatalog();
    await load();
  }

  async function installFromFile() {
    const file = await open({
      multiple: false,
      filters: [{ name: "Plugin", extensions: ["zip", "dll"] }],
    });
    if (typeof file === "string") {
      setBusy(true);
      await daemon.installPlugin(file).catch(() => {});
      setBusy(false);
      await afterChange();
    }
  }

  async function download(meta: PluginMetadata) {
    setBusy(true);
    await daemon.downloadPlugin(meta).catch(() => {});
    setBusy(false);
    await afterChange();
  }

  return (
    <Dialog {...d}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Plugin Manager</DialogTitle>
        </DialogHeader>

        <Tabs defaultValue="installed">
          <div className="flex items-center justify-between">
            <TabsList>
              <TabsTrigger value="installed">Installed</TabsTrigger>
              <TabsTrigger value="repository">Repository</TabsTrigger>
            </TabsList>
            <Button size="sm" variant="outline" disabled={busy} onClick={installFromFile}>
              Install from file…
            </Button>
          </div>

          <TabsContent value="installed" className="max-h-96 overflow-auto">
            {installed.length === 0 ? (
              <Empty>No plugins installed.</Empty>
            ) : (
              installed.map((p, i) => <Row key={i} meta={p} />)
            )}
          </TabsContent>

          <TabsContent value="repository" className="max-h-96 overflow-auto">
            {repo.length === 0 ? (
              <Empty>Repository unavailable (needs updated daemon or network).</Empty>
            ) : (
              repo.map((p, i) => (
                <Row
                  key={i}
                  meta={p}
                  action={
                    <Button size="sm" disabled={busy} onClick={() => download(p)}>
                      {p.Installed ? "Reinstall" : "Download"}
                    </Button>
                  }
                />
              ))
            )}
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  );
}

function Row({ meta, action }: { meta: PluginMetadata; action?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between border-b py-2 last:border-0">
      <div>
        <div className="text-sm font-medium">{meta.Name}</div>
        <div className="text-xs text-muted-foreground">
          {meta.Owner ? `${meta.Owner} · ` : ""}
          {meta.Description ?? ""}
        </div>
      </div>
      {action}
    </div>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return <p className="py-6 text-center text-sm text-muted-foreground">{children}</p>;
}
