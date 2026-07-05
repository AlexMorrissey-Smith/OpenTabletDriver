import { currentProfile, useStore } from "@/lib/store";
import { PluginCollectionEditor } from "../PluginCollectionEditor";

export function FiltersPage() {
  const profile = useStore(currentProfile);
  const updateProfile = useStore((s) => s.updateProfile);
  if (!profile) return null;

  return (
    <PluginCollectionEditor
      category="Filters"
      items={profile.Filters ?? []}
      mutate={(fn) => updateProfile((p) => fn(p.Filters))}
    />
  );
}
