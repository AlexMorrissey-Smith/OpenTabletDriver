using System;
using System.Collections.Generic;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginIgnore]
    public class BindingHandler : IPositionedPipelineElement<IDeviceReport>
    {
        public BindingHandler(TabletReference tablet)
        {
            this.tablet = tablet;
            Global = ActiveBindingSet = new BindingSet(tablet);
        }

        /// <summary>The default binding set ("All other applications" in Wacom terms).</summary>
        public BindingSet Global { get; private set; }

        /// <summary>App-specific binding sets, keyed by bundle identifier.</summary>
        public IReadOnlyDictionary<string, BindingSet> AppOverrides { get; private set; } = new Dictionary<string, BindingSet>();

        /// <summary>The binding set currently being dispatched through, based on the foreground app.</summary>
        public BindingSet ActiveBindingSet { get; private set; }

        public PipelinePosition Position => PipelinePosition.PostTransform;

        private readonly TabletReference tablet;

        public event Action<IDeviceReport?>? Emit;

        private bool _suppressTabletOutput;
        private long _suppressUntil;
        private const long SuppressGraceMs = 100; // ride over single-frame Bluetooth button-bit drops

        private string? _activeBundleId;
        private long _lastForegroundCheck;
        private const long ForegroundCheckIntervalMs = 150; // foreground app changes far slower than tablet report rate

        public void SetBindingSets(BindingSet global, IReadOnlyDictionary<string, BindingSet> appOverrides)
        {
            Global = global;
            AppOverrides = appOverrides;
            ActiveBindingSet = global;
            _activeBundleId = null;
            _lastForegroundCheck = 0;
        }

        public void Consume(IDeviceReport? report)
        {
            _suppressTabletOutput = false;

            if (report != null)
            {
                UpdateActiveBindingSet(report);
                HandleBinding(report);
            }

            // While a report binding (e.g. Pen Scroll) is held, swallow positional pen
            // reports so the cursor freezes and no tip clicks/drags leak through. The
            // binding emits scroll itself; not re-posting position avoids flooding the
            // OS event queue (the cause of laggy scrolling).
            if (_suppressTabletOutput && report is ITabletReport)
                report = null;

            Emit?.Invoke(report);
        }

        // Picks the binding set for the current foreground app, throttled since
        // NSWorkspace lookups cost far more than reading a cached field and the
        // foreground app changes orders of magnitude slower than tablet reports.
        private void UpdateActiveBindingSet(IDeviceReport report)
        {
            if (AppOverrides.Count == 0)
                return;

            long now = Environment.TickCount64;
            if (now - _lastForegroundCheck < ForegroundCheckIntervalMs)
                return;
            _lastForegroundCheck = now;

            bool detected = DesktopInterop.ForegroundApp.TryGetForegroundApp(out var id, out _);
            string? bundleId = detected ? id : null;
            if (bundleId == _activeBundleId)
                return;

            _activeBundleId = bundleId;

            BindingSet? overrideSet = null;
            bool hasOverride = bundleId != null && AppOverrides.TryGetValue(bundleId, out overrideSet);
            var nextSet = hasOverride ? overrideSet! : Global;

            Log.Write(nameof(BindingHandler),
                $"Foreground app changed to '{bundleId}' → {(hasOverride ? "app-specific" : "All Applications")} bindings",
                LogLevel.Debug);

            if (!ReferenceEquals(nextSet, ActiveBindingSet))
            {
                // Don't leave a key/button stuck down from the outgoing app's bindings.
                ActiveBindingSet.ForceReleaseAll(tablet, report);
                ActiveBindingSet = nextSet;
            }
        }

        public void HandleBinding(IDeviceReport report)
        {
            if (report is IEraserReport eraserReport)
                _isEraser = eraserReport.Eraser;
            if (report is ITabletReport tabletReport)
                HandleTabletReport(tablet, tablet.Properties.Specifications.Pen, tabletReport);
            if (report is IAuxReport auxReport)
                HandleAuxiliaryReport(tablet, auxReport);
            if (report is IMouseReport mouseReport)
                HandleMouseReport(tablet, mouseReport);
            if (report is IWheelButtonReport wheelButtonReport)
                HandleWheelButtonReport(tablet, wheelButtonReport);
            if (report is IAbsoluteWheelReport absoluteWheelReport)
                HandleAbsoluteWheelReport(tablet, absoluteWheelReport);
            if (report is IRelativeWheelReport relativeWheelReport)
                HandleRelativeWheelReport(tablet, relativeWheelReport);
            if (report is OutOfRangeReport)
                HandleOutOfRangeReport(tablet, report);
        }

        private bool _isEraser;

        private readonly HashSet<int> _triedRelativeWheels = [];

        private void HandleRelativeWheelReport(TabletReference tabletReference, IRelativeWheelReport relativeWheelReport)
        {
            for (int i = 0; i < relativeWheelReport.AnalogDeltas.Length; i++)
            {
                int reportDelta = relativeWheelReport.AnalogDeltas[i];

                if (ActiveBindingSet.Wheels.TryGetValue(i, out var wheelBinding))
                    wheelBinding.HandleRelativeWheel(tabletReference, relativeWheelReport, reportDelta);
                else if (reportDelta != 0 && _triedRelativeWheels.Add(i))
                {
                    Log.Write(nameof(BindingHandler),
                        $"Tablet '{tablet.Properties.Name}' is missing wheel declarations for wheel '{i}' to handle its wheel bindings",
                        LogLevel.Warning);
                }
            }
        }

        private readonly HashSet<int> _triedAbsoluteWheels = [];

        private void HandleAbsoluteWheelReport(TabletReference tabletReference, IAbsoluteWheelReport absoluteWheelReport)
        {
            for (int i = 0; i < absoluteWheelReport.AnalogPositions.Length; i++)
            {
                uint? reportPosition = absoluteWheelReport.AnalogPositions[i];

                if (ActiveBindingSet.Wheels.TryGetValue(i, out var wheelBinding))
                    wheelBinding.HandleAbsoluteWheel(tabletReference, absoluteWheelReport, reportPosition);
                else if (reportPosition != null && _triedAbsoluteWheels.Add(i))
                {
                    Log.Write(nameof(BindingHandler),
                        $"Tablet '{tablet.Properties.Name}' is missing wheel declarations for wheel '{i}' to handle its wheel bindings",
                        LogLevel.Warning);
                }
            }
        }

        private readonly HashSet<int> _triedWheelButtons = [];

        private void HandleWheelButtonReport(TabletReference tabletReference, IWheelButtonReport wheelButtonReport)
        {
            for (int i = 0; i < wheelButtonReport.WheelButtons.Length; i++)
            {
                if (ActiveBindingSet.Wheels.TryGetValue(i, out var wheelBinding))
                {
                    bool[] wheelButton = wheelButtonReport.WheelButtons[i];
                    HandleBindingCollection(tabletReference, wheelButtonReport, wheelBinding.WheelButtons, wheelButton);
                }
                else if (_triedWheelButtons.Add(i))
                {
                    Log.Write(nameof(BindingHandler),
                        $"Tablet '{tablet.Properties.Name}' is missing wheel declarations for wheel '{i}' to handle its wheel button bindings",
                        LogLevel.Warning);
                }
            }
        }

        private void HandleOutOfRangeReport(TabletReference tablet, IDeviceReport report)
        {
            ActiveBindingSet.Tip?.Invoke(tablet, report, 0);
            ActiveBindingSet.Eraser?.Invoke(tablet, report, 0);

            for (var i = 0; i < ActiveBindingSet.PenButtons.Count; i++)
            {
                if (ActiveBindingSet.PenButtons.TryGetValue(i, out var binding))
                    binding?.Invoke(tablet, report, false);
            }
        }

        private void HandleTabletReport(TabletReference tablet, PenSpecifications pen, ITabletReport report)
        {
            // If a held pen button drives a report binding (Pen Scroll), suppress the tip/eraser
            // so contact doesn't click or drag while scrolling, and mark the report for swallowing.
            // Hold suppression for a short grace window: the Bluetooth button bit occasionally
            // drops for a single frame, and without grace that frame leaks the pen position to
            // the cursor (it jumps). Grace keeps the cursor frozen across such glitches.
            bool scrollHeld = AnyHeldButtonIsReportBinding(report.PenButtons);
            if (scrollHeld)
                _suppressUntil = Environment.TickCount64 + SuppressGraceMs;
            bool suppress = scrollHeld || Environment.TickCount64 < _suppressUntil;
            _suppressTabletOutput = suppress;

            uint realPressure = report.Pressure;
            float pressurePercent = suppress ? 0f : (float)report.Pressure / (float)pen.MaxPressure * 100f;
            if (_isEraser)
                ActiveBindingSet.Eraser?.Invoke(tablet, report, pressurePercent);
            else
                ActiveBindingSet.Tip?.Invoke(tablet, report, pressurePercent);

            // Tip/Eraser threshold state zeroes report.Pressure when not pressed; restore the
            // real value so a report binding (Pen Scroll) can still detect pen contact.
            if (suppress)
                report.Pressure = realPressure;

            HandleBindingCollection(tablet, report, ActiveBindingSet.PenButtons, report.PenButtons);
        }

        private bool AnyHeldButtonIsReportBinding(bool[] states)
        {
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] && ActiveBindingSet.PenButtons.TryGetValue(i, out var binding) && binding?.Binding is IReportBinding)
                    return true;
            }
            return false;
        }

        private void HandleAuxiliaryReport(TabletReference tablet, IAuxReport report)
        {
            HandleBindingCollection(tablet, report, ActiveBindingSet.AuxButtons, report.AuxButtons);
        }

        private void HandleMouseReport(TabletReference tablet, IMouseReport report)
        {
            HandleBindingCollection(tablet, report, ActiveBindingSet.MouseButtons, report.MouseButtons);

            ActiveBindingSet.MouseScrollDown?.Invoke(tablet, report, report.Scroll.Y < 0);
            ActiveBindingSet.MouseScrollUp?.Invoke(tablet, report, report.Scroll.Y > 0);
        }

        private static void HandleBindingCollection(TabletReference tablet, IDeviceReport report, Dictionary<int, BindingState?> bindings, bool[] newStates)
        {
            for (int i = 0; i < newStates.Length; i++)
            {
                if (bindings.TryGetValue(i, out var binding))
                    binding?.Invoke(tablet, report, newStates[i]);
            }
        }
    }
}
