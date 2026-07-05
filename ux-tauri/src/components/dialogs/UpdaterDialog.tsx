import { useEffect, useState } from "react";
import { daemon } from "@/lib/daemon";
import { useDialog } from "@/lib/ui";
import type { SerializedUpdateInfo } from "@/lib/types";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";

export function UpdaterDialog() {
  const d = useDialog("updater");
  const [state, setState] = useState<"checking" | "available" | "none" | "installing">("checking");
  const [info, setInfo] = useState<SerializedUpdateInfo | null>(null);

  useEffect(() => {
    if (!d.open) return;
    setState("checking");
    daemon
      .checkForUpdates()
      .then((u) => {
        setInfo(u);
        setState(u ? "available" : "none");
      })
      .catch(() => setState("none"));
  }, [d.open]);

  return (
    <Dialog {...d}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Updates</DialogTitle>
        </DialogHeader>
        <div className="text-sm">
          {state === "checking" && <p>Checking for updates…</p>}
          {state === "none" && <p>No updates are available.</p>}
          {state === "available" && <p>Update available: {info?.Version}</p>}
          {state === "installing" && <p>Installing…</p>}
        </div>
        {state === "available" && (
          <DialogFooter>
            <Button
              onClick={() => {
                setState("installing");
                daemon.installUpdate().catch(() => setState("available"));
              }}
            >
              Install update
            </Button>
          </DialogFooter>
        )}
      </DialogContent>
    </Dialog>
  );
}
