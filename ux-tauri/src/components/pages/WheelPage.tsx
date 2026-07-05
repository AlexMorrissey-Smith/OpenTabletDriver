import { currentBindings, useStore } from "@/lib/store";
import { BindingButton } from "../BindingButton";
import { BindingArray } from "../BindingArray";
import { Section } from "../Section";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";

export function WheelPage() {
  const bindings = useStore(currentBindings);
  const updateBindings = useStore((s) => s.updateBindings);
  if (!bindings) return null;

  const wheels = bindings.WheelBindings ?? [];
  if (wheels.length === 0) {
    return <p className="text-sm text-muted-foreground">This tablet has no wheels.</p>;
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      {wheels.map((wheel, w) => (
        <Section key={w} title={wheels.length > 1 ? `Wheel ${w + 1}` : "Wheel"}>
          <div className="space-y-3">
            <div className="grid gap-2 sm:grid-cols-2">
              <BindingButton
                label="Clockwise"
                store={wheel.ClockwiseRotation}
                onChange={(v) =>
                  updateBindings((b) => void (b.WheelBindings[w].ClockwiseRotation = v))
                }
              />
              <BindingButton
                label="Counter-clockwise"
                store={wheel.CounterClockwiseRotation}
                onChange={(v) =>
                  updateBindings((b) => void (b.WheelBindings[w].CounterClockwiseRotation = v))
                }
              />
            </div>
            <div className="flex flex-wrap gap-4">
              <div className="flex items-center gap-2">
                <Label className="text-sm">CW threshold (°)</Label>
                <Input
                  type="number"
                  className="w-24"
                  value={wheel.ClockwiseActivationThreshold}
                  onChange={(e) =>
                    updateBindings(
                      (b) =>
                        void (b.WheelBindings[w].ClockwiseActivationThreshold = Number(
                          e.target.value,
                        )),
                    )
                  }
                />
              </div>
              <div className="flex items-center gap-2">
                <Label className="text-sm">CCW threshold (°)</Label>
                <Input
                  type="number"
                  className="w-24"
                  value={wheel.CounterClockwiseActivationThreshold}
                  onChange={(e) =>
                    updateBindings(
                      (b) =>
                        void (b.WheelBindings[w].CounterClockwiseActivationThreshold = Number(
                          e.target.value,
                        )),
                    )
                  }
                />
              </div>
            </div>
            <div>
              <Label className="text-sm mb-2 block">Wheel buttons</Label>
              <BindingArray
                items={wheel.WheelButtons}
                labelPrefix="Button"
                onSet={(i, v) =>
                  updateBindings((b) => void (b.WheelBindings[w].WheelButtons[i] = v!))
                }
              />
            </div>
          </div>
        </Section>
      ))}
    </div>
  );
}
