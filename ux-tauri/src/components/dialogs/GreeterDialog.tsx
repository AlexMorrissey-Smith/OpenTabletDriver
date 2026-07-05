import { useState } from "react";
import { useDialog } from "@/lib/ui";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";

const PAGES = [
  {
    title: "Welcome to OpenTabletDriver",
    body: "This quick guide covers the basics. You can reopen it any time from the menu.",
  },
  {
    title: "Area editor",
    body: "On the Tablet page, drag the highlighted area to move it, or drag a corner to resize. Set exact values with the numeric fields, including rotation.",
  },
  {
    title: "Bindings",
    body: "On Pen, Buttons, Mouse and Wheel pages, click a binding to choose what it does — keys, mouse buttons, scrolling and more.",
  },
  {
    title: "App-specific bindings",
    body: "Use the “Bindings for” selector at the top to add per-application overrides, starting as a copy of your global bindings.",
  },
  {
    title: "Plugins & presets",
    body: "Add filters and tools from the Plugin Manager, and save your configuration as a preset — presets are available from the tray icon.",
  },
];

export function GreeterDialog() {
  const d = useDialog("greeter");
  const [page, setPage] = useState(0);
  const current = PAGES[page];
  const last = page === PAGES.length - 1;

  return (
    <Dialog
      open={d.open}
      onOpenChange={(o) => {
        d.onOpenChange(o);
        if (!o) setPage(0);
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{current.title}</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-muted-foreground">{current.body}</p>
        <DialogFooter>
          <Button variant="ghost" disabled={page === 0} onClick={() => setPage((p) => p - 1)}>
            Back
          </Button>
          {last ? (
            <Button onClick={() => d.onOpenChange(false)}>Close</Button>
          ) : (
            <Button onClick={() => setPage((p) => p + 1)}>Next</Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
