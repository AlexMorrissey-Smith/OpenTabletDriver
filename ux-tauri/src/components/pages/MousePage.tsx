import { currentBindings, useStore } from "@/lib/store";
import { BindingButton } from "../BindingButton";
import { BindingArray } from "../BindingArray";
import { Section } from "../Section";

export function MousePage() {
  const bindings = useStore(currentBindings);
  const updateBindings = useStore((s) => s.updateBindings);
  if (!bindings) return null;

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <Section title="Mouse buttons">
        <BindingArray
          items={bindings.MouseButtons}
          labelPrefix="Button"
          onSet={(i, v) => updateBindings((b) => void (b.MouseButtons[i] = v!))}
        />
      </Section>

      <Section title="Scroll">
        <div className="grid gap-2 sm:grid-cols-2">
          <BindingButton
            label="Scroll up"
            store={bindings.MouseScrollUp}
            onChange={(v) => updateBindings((b) => void (b.MouseScrollUp = v))}
          />
          <BindingButton
            label="Scroll down"
            store={bindings.MouseScrollDown}
            onChange={(v) => updateBindings((b) => void (b.MouseScrollDown = v))}
          />
        </div>
      </Section>
    </div>
  );
}
