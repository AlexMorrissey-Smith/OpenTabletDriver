using System.Collections.Generic;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    /// <summary>
    /// One fully-constructed set of bindings (global, or one per app-specific override).
    /// <see cref="BindingHandler"/> dispatches reports through whichever set is active.
    /// </summary>
    public class BindingSet
    {
        public BindingSet(TabletReference tablet)
        {
            int wheelIndex = 0;
            foreach (var wheel in tablet.Properties.Specifications.Wheels ?? [])
                Wheels.Add(wheelIndex++, new WheelBindings(wheel));
        }

        public ThresholdBindingState? Tip { set; get; }
        public ThresholdBindingState? Eraser { set; get; }

        public Dictionary<int, BindingState?> PenButtons { get; } = new Dictionary<int, BindingState?>();
        public Dictionary<int, BindingState?> AuxButtons { get; } = new Dictionary<int, BindingState?>();
        public Dictionary<int, BindingState?> MouseButtons { get; } = new Dictionary<int, BindingState?>();

        public BindingState? MouseScrollDown { set; get; }
        public BindingState? MouseScrollUp { set; get; }

        public Dictionary<int, WheelBindings> Wheels { get; } = new Dictionary<int, WheelBindings>();

        // Releases every currently-held binding in this set so swapping the active set
        // mid-press (foreground app changed while a button/wheel was held) never leaves
        // a key/button stuck down.
        public void ForceReleaseAll(TabletReference tablet, IDeviceReport report)
        {
            Tip?.ForceRelease(tablet, report);
            Eraser?.ForceRelease(tablet, report);

            foreach (var binding in PenButtons.Values)
                binding?.ForceRelease(tablet, report);
            foreach (var binding in AuxButtons.Values)
                binding?.ForceRelease(tablet, report);
            foreach (var binding in MouseButtons.Values)
                binding?.ForceRelease(tablet, report);

            MouseScrollUp?.ForceRelease(tablet, report);
            MouseScrollDown?.ForceRelease(tablet, report);

            foreach (var wheel in Wheels.Values)
            {
                foreach (var binding in wheel.WheelButtons.Values)
                    binding?.ForceRelease(tablet, report);

                wheel.ClockwiseRotation?.ForceRelease(tablet, report);
                wheel.CounterClockwiseRotation?.ForceRelease(tablet, report);
                wheel.Reset();
            }
        }
    }
}
