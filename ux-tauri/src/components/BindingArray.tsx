import type { PluginSettingStoreCollection } from "@/lib/types";
import { BindingButton } from "./BindingButton";

interface Props {
  items: PluginSettingStoreCollection | undefined;
  labelPrefix: string;
  onSet: (index: number, store: import("@/lib/types").PluginSettingStore | null) => void;
  emptyText?: string;
}

/** Renders a fixed list of binding slots (pen/aux/mouse/wheel buttons). Slots
 *  map 1:1 to physical buttons, so we edit existing entries by index. */
export function BindingArray({ items, labelPrefix, onSet, emptyText }: Props) {
  if (!items || items.length === 0) {
    return <p className="text-sm text-muted-foreground">{emptyText ?? "None."}</p>;
  }
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      {items.map((store, i) => (
        <BindingButton
          key={i}
          label={`${labelPrefix} ${i + 1}`}
          store={store}
          onChange={(v) => onSet(i, v)}
        />
      ))}
    </div>
  );
}
