using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    public class BindingState
    {
        public IBinding? Binding { set; get; }

        protected bool PreviousState { set; get; }

        public bool RequiresPenPressure { set; get; } // "drag bindings"

        public virtual void Invoke(TabletReference tablet, IDeviceReport report, bool newState)
        {
            // Check if drag binding is needed or unnecessary
            // NOTE: report _must_ include pressure to work properly
            // TODO: use relevant threshold instead of '0'
            bool pressureThresholdIsMetOrUnneeded =
                !RequiresPenPressure || (RequiresPenPressure && report is ITabletReport { Pressure: > 0 });

            if (Binding is IStateBinding stateBinding)
            {
                if (newState && !PreviousState)
                {
                    if (pressureThresholdIsMetOrUnneeded)
                        stateBinding.Press(tablet, report);
                }
                else if (!newState && PreviousState)
                    stateBinding.Release(tablet, report);
            }

            if (!newState || pressureThresholdIsMetOrUnneeded) // don't update state to true without threshold
                PreviousState = newState;

            // feed continuous reports to bindings that opt in (e.g. pen-motion scroll)
            if (newState && Binding is IReportBinding reportBinding)
                reportBinding.OnReport(tablet, report);
        }

        // Releases a held binding without a corresponding physical release, so switching
        // the active binding set (e.g. app-specific bindings on foreground-app change)
        // never leaves a key/button stuck down.
        public void ForceRelease(TabletReference tablet, IDeviceReport report)
        {
            if (PreviousState && Binding is IStateBinding stateBinding)
                stateBinding.Release(tablet, report);

            PreviousState = false;
        }
    }
}
