import { useStore } from "@/lib/store";
import { PluginCollectionEditor } from "../PluginCollectionEditor";

export function ToolsPage() {
  const settings = useStore((s) => s.settings);
  const update = useStore((s) => s.update);
  if (!settings) return null;

  return (
    <PluginCollectionEditor
      category="Tools"
      items={settings.Tools ?? []}
      mutate={(fn) => update((s) => fn(s.Tools))}
    />
  );
}
