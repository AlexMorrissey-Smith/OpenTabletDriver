import { useEffect, useState } from "react";
import { daemon } from "@/lib/daemon";
import { useDialog } from "@/lib/ui";
import type { SerializedDeviceEndpoint } from "@/lib/types";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export function DeviceStringReaderDialog() {
  const d = useDialog("strings");
  const [devices, setDevices] = useState<SerializedDeviceEndpoint[]>([]);
  const [vid, setVid] = useState(0);
  const [pid, setPid] = useState(0);
  const [index, setIndex] = useState(1);
  const [result, setResult] = useState("");

  useEffect(() => {
    if (d.open) daemon.getDevices().then(setDevices).catch(() => {});
  }, [d.open]);

  function pickDevice(path: string) {
    const dev = devices.find((x) => x.DevicePath === path);
    if (dev) {
      setVid(dev.VendorID);
      setPid(dev.ProductID);
    }
  }

  async function send() {
    try {
      setResult(await daemon.requestDeviceString(vid, pid, index));
    } catch (e) {
      setResult(String(e));
    }
  }

  return (
    <Dialog {...d}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Device String Reader</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <Select onValueChange={(v) => v && pickDevice(String(v))}>
            <SelectTrigger>
              <SelectValue placeholder="Connected devices…" />
            </SelectTrigger>
            <SelectContent>
              {devices.map((dev) => (
                <SelectItem key={dev.DevicePath} value={dev.DevicePath}>
                  {dev.FriendlyName || dev.ProductName || dev.DevicePath}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <div className="grid grid-cols-3 gap-2">
            <Field label="Vendor ID" value={vid} onChange={setVid} />
            <Field label="Product ID" value={pid} onChange={setPid} />
            <Field label="String index" value={index} onChange={setIndex} />
          </div>

          <Button onClick={send}>Send request</Button>

          <div className="space-y-1">
            <Label>Result</Label>
            <pre className="min-h-16 rounded-md border bg-muted/30 p-2 text-xs break-all whitespace-pre-wrap">
              {result}
            </pre>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function Field({ label, value, onChange }: { label: string; value: number; onChange: (v: number) => void }) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">{label}</Label>
      <Input type="number" value={value} onChange={(e) => onChange(Number(e.target.value))} />
    </div>
  );
}
