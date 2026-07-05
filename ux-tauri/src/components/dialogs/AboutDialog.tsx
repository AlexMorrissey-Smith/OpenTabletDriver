import { openUrl } from "@tauri-apps/plugin-opener";
import { useStore } from "@/lib/store";
import { useDialog } from "@/lib/ui";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";

export function AboutDialog() {
  const d = useDialog("about");
  const revision = useStore((s) => s.settings?.Revision);

  return (
    <Dialog {...d}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>OpenTabletDriver</DialogTitle>
        </DialogHeader>
        <div className="space-y-2 text-sm">
          <p className="text-muted-foreground">Open source, cross-platform tablet driver.</p>
          {revision ? <p>Version {revision}</p> : null}
          <div className="flex gap-2 pt-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => openUrl("https://github.com/OpenTabletDriver/OpenTabletDriver")}
            >
              GitHub
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => openUrl("https://opentabletdriver.net")}
            >
              Website
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
