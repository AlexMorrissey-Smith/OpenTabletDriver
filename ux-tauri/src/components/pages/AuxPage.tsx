import { currentBindings, useStore } from "@/lib/store";
import { BindingArray } from "../BindingArray";
import { Section } from "../Section";

export function AuxPage() {
  const bindings = useStore(currentBindings);
  const updateBindings = useStore((s) => s.updateBindings);
  if (!bindings) return null;

  return (
    <div className="mx-auto max-w-3xl">
      <Section title="Auxiliary buttons">
        <BindingArray
          items={bindings.AuxButtons}
          labelPrefix="Button"
          emptyText="This tablet has no auxiliary (express) buttons."
          onSet={(i, v) => updateBindings((b) => void (b.AuxButtons[i] = v!))}
        />
      </Section>
    </div>
  );
}
