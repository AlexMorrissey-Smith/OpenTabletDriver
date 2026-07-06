import { currentBindings, useStore } from "@/lib/store";
import { BindingButton } from "../BindingButton";
import { BindingArray } from "../BindingArray";
import { PressureCurve } from "../PressureCurve";
import { Section } from "../Section";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";

export function PenPage() {
  const bindings = useStore(currentBindings);
  const updateBindings = useStore((s) => s.updateBindings);
  if (!bindings) return null;

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <Section title="Tip">
        <div className="space-y-3">
          <BindingButton
            store={bindings.TipButton}
            onChange={(v) => updateBindings((b) => void (b.TipButton = v))}
          />
          <div className="flex items-center gap-3">
            <Label className="text-sm w-40">Activation threshold</Label>
            <Input
              type="number"
              className="w-32"
              min={0}
              max={100}
              value={bindings.TipActivationThreshold}
              onChange={(e) =>
                updateBindings((b) => void (b.TipActivationThreshold = Number(e.target.value)))
              }
            />
            <span className="text-sm text-muted-foreground">% pressure</span>
          </div>
        </div>
      </Section>

      <Section title="Eraser">
        <div className="space-y-3">
          <BindingButton
            store={bindings.EraserButton}
            onChange={(v) => updateBindings((b) => void (b.EraserButton = v))}
          />
          <div className="flex items-center gap-3">
            <Label className="text-sm w-40">Activation threshold</Label>
            <Input
              type="number"
              className="w-32"
              min={0}
              max={100}
              value={bindings.EraserActivationThreshold}
              onChange={(e) =>
                updateBindings((b) => void (b.EraserActivationThreshold = Number(e.target.value)))
              }
            />
            <span className="text-sm text-muted-foreground">% pressure</span>
          </div>
        </div>
      </Section>

      <Section
        title="Pressure curve"
        description="Shape how physical tip pressure maps to output pressure."
      >
        <PressureCurve />
      </Section>

      <Section title="Pen buttons">
        <BindingArray
          items={bindings.PenButtons}
          labelPrefix="Button"
          emptyText="This tablet has no pen buttons."
          onSet={(i, v) => updateBindings((b) => void (b.PenButtons[i] = v!))}
        />
      </Section>

      <Section title="Options">
        <div className="space-y-3">
          <ToggleRow
            label="Disable pressure"
            checked={bindings.DisablePressure}
            onChange={(v) => updateBindings((b) => void (b.DisablePressure = v))}
          />
          <ToggleRow
            label="Disable tilt"
            checked={bindings.DisableTilt}
            onChange={(v) => updateBindings((b) => void (b.DisableTilt = v))}
          />
          <ToggleRow
            label="Enable drag bindings (require pen pressure)"
            checked={bindings.EnableDragBindings}
            onChange={(v) => updateBindings((b) => void (b.EnableDragBindings = v))}
          />
        </div>
      </Section>
    </div>
  );
}

function ToggleRow({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <div className="flex items-center justify-between">
      <Label className="text-sm">{label}</Label>
      <Switch checked={checked} onCheckedChange={onChange} />
    </div>
  );
}
