import { useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { openUrl } from "@tauri-apps/plugin-opener";
import { useStore } from "@/lib/store";
import { useUI } from "@/lib/ui";
import { daemon } from "@/lib/daemon";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Menu } from "lucide-react";

export function AppMenu() {
  const open = useUI((s) => s.open);
  const reload = useStore((s) => s.reload);
  const [presetOpen, setPresetOpen] = useState(false);
  const [presetName, setPresetName] = useState("");

  async function resetDefaults() {
    await daemon.resetSettings().catch(() => {});
    await reload();
  }

  async function savePreset() {
    const settings = useStore.getState().settings;
    if (settings && presetName.trim()) {
      await daemon.savePreset(presetName.trim(), settings).catch(() => {});
    }
    setPresetName("");
    setPresetOpen(false);
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger render={<Button variant="ghost" size="icon" />}>
          <Menu className="size-4" />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-56">
          <DropdownMenuItem onClick={() => daemon.detectTablets().catch(() => {})}>
            Detect tablets
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => setPresetOpen(true)}>Save as preset…</DropdownMenuItem>
          <DropdownMenuItem onClick={resetDefaults}>Reset to defaults</DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem onClick={() => open("plugins")}>Plugin Manager…</DropdownMenuItem>
          <DropdownMenuItem onClick={() => open("debugger")}>Tablet Debugger…</DropdownMenuItem>
          <DropdownMenuItem onClick={() => open("strings")}>Device String Reader…</DropdownMenuItem>
          <DropdownMenuItem onClick={() => open("updater")}>Check for Updates…</DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem
            onClick={() => invoke("open_permission_pane", { pane: "accessibility" })}
          >
            Grant Accessibility…
          </DropdownMenuItem>
          <DropdownMenuItem
            onClick={() => invoke("open_permission_pane", { pane: "input-monitoring" })}
          >
            Grant Input Monitoring…
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem onClick={() => open("greeter")}>Show Guide…</DropdownMenuItem>
          <DropdownMenuItem
            onClick={() => openUrl("https://opentabletdriver.net/Wiki").catch(() => {})}
          >
            Open Wiki
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => open("about")}>About</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={presetOpen} onOpenChange={setPresetOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Save as preset</DialogTitle>
          </DialogHeader>
          <div className="space-y-1">
            <Label>Preset name</Label>
            <Input
              value={presetName}
              onChange={(e) => setPresetName(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && savePreset()}
            />
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setPresetOpen(false)}>
              Cancel
            </Button>
            <Button onClick={savePreset}>Save</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
