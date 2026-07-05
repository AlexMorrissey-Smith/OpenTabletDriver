import { useState } from "react";
import { useStore } from "@/lib/store";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const LEVEL_NAME: Record<number, string> = {
  0: "Debug",
  1: "Verbose",
  2: "Info",
  3: "Warning",
  4: "Error",
  5: "Fatal",
};

const LEVEL_CLASS: Record<number, string> = {
  3: "text-yellow-500",
  4: "text-destructive",
  5: "text-destructive",
};

export function ConsolePage() {
  const log = useStore((s) => s.log);
  const clearLog = useStore((s) => s.clearLog);
  const [minLevel, setMinLevel] = useState(0);

  const filtered = log.filter((m) => m.Level >= minLevel);

  function copyAll() {
    const text = filtered
      .map((m) => `${m.Time} [${LEVEL_NAME[m.Level]}] (${m.Group}) ${m.Message}`)
      .join("\n");
    navigator.clipboard.writeText(text).catch(() => {});
  }

  return (
    <div className="flex h-full flex-col gap-3">
      <div className="flex items-center gap-2">
        <Select value={String(minLevel)} onValueChange={(v) => setMinLevel(Number(v))}>
          <SelectTrigger className="w-40">
            <SelectValue>{(v: string) => `${LEVEL_NAME[Number(v)] ?? v}+`}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {Object.entries(LEVEL_NAME).map(([v, name]) => (
              <SelectItem key={v} value={v}>
                {name}+
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button variant="outline" size="sm" onClick={copyAll}>
          Copy all
        </Button>
        <Button variant="ghost" size="sm" onClick={clearLog}>
          Clear
        </Button>
        <span className="text-xs text-muted-foreground ml-auto">{filtered.length} messages</span>
      </div>

      <div className="flex-1 overflow-auto rounded-md border bg-muted/30 font-mono text-xs">
        <table className="w-full border-collapse">
          <tbody>
            {filtered.map((m, i) => (
              <tr key={i} className="border-b border-border/50 align-top">
                <td className="whitespace-nowrap px-2 py-0.5 text-muted-foreground">
                  {m.Time?.slice(11, 19)}
                </td>
                <td className={`px-2 py-0.5 ${LEVEL_CLASS[m.Level] ?? ""}`}>
                  {LEVEL_NAME[m.Level] ?? m.Level}
                </td>
                <td className="whitespace-nowrap px-2 py-0.5 text-muted-foreground">{m.Group}</td>
                <td className="px-2 py-0.5 break-all">{m.Message}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
