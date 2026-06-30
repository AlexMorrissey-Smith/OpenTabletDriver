using System;
using System.Collections.Generic;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

#nullable enable

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class PenScrollBinding : IReportBinding
    {
        private const string PLUGIN_NAME = "Pen Scroll";

        private enum ScrollAxis { Both, Vertical, Horizontal }

        private ScrollAxis _axis = ScrollAxis.Both;
        private bool _held;
        private Vector2 _lastReal;
        private float _accX, _accY;

        [Resolved]
        public IMouseScrollHandler? Pointer { set; get; }

        [OnDependencyLoad]
        public void VerifyInitialization()
        {
            if (Pointer == null)
                Log.Write(PLUGIN_NAME,
                    $"{nameof(IMouseScrollHandler)} unavailable. Your selected output mode is incompatible",
                    LogLevel.Error);
        }

        [Property("Direction"), DefaultPropertyValue("Both"), PropertyValidated(nameof(ValidDirections))]
        public string Direction
        {
            get => _axis.ToString();
            set
            {
                if (Enum.TryParse(value, out ScrollAxis axis))
                    _axis = axis;
                else
                    Log.Write(PLUGIN_NAME, $"Invalid direction '{value}', defaulting to 'Both'", LogLevel.Warning);
            }
        }

        [Property("Sensitivity"),
         DefaultPropertyValue(1.5f),
         ToolTip("Scroll units emitted per pixel of pen movement. Higher = faster scroll.\n\n" +
                 "Tuned for macOS (pixel-unit scroll). On Windows/Linux a tick is 120, " +
                 "so you may want a much larger value there.")]
        // ponytail: single default; pixel units on macOS, 120=notch on Win/Linux — bump per platform if it feels off
        public float Sensitivity { get; set; } = 1.5f;

        [Property("Natural"),
         DefaultPropertyValue(true),
         ToolTip("Natural scrolling (like Apple trackpads): content follows the pen. " +
                 "Turn off for traditional scrolling.")]
        public bool Natural { get; set; } = true;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (report is not IAbsolutePositionReport posReport)
                return;
            _lastReal = posReport.Position;
            _accX = _accY = 0;
            _held = true;
        }

        public void Release(TabletReference tablet, IDeviceReport report) => _held = false;

        public void OnReport(TabletReference tablet, IDeviceReport report)
        {
            if (!_held || report is not IAbsolutePositionReport posReport)
                return;

            var real = posReport.Position;
            var delta = real - _lastReal;
            _lastReal = real; // always track so resuming contact doesn't jump

            // Contact-only (Wacom-style): scroll only while the tip is touching. Hovering
            // with the button held keeps the cursor frozen but does not scroll. BindingHandler
            // restores the real pressure for us (the tip threshold otherwise zeroes it).
            if (report is not ITabletReport { Pressure: > 0 })
                return;

            // Natural: content follows the pen (pen down → scroll up). Traditional: opposite.
            float sign = Natural ? 1f : -1f;

            if (_axis != ScrollAxis.Horizontal)
            {
                int ticks = AccumulateScroll(ref _accY, delta.Y, Sensitivity);
                if (ticks != 0)
                    Pointer?.ScrollVertically((int)(sign * ticks));
            }

            if (_axis != ScrollAxis.Vertical)
            {
                int ticks = AccumulateScroll(ref _accX, delta.X, Sensitivity);
                if (ticks != 0)
                    Pointer?.ScrollHorizontally((int)(-sign * ticks)); // horizontal is mirrored vs vertical
            }

            // Cursor freeze + click suppression are handled by BindingHandler swallowing
            // the report; we just emit the scroll here and flush it ourselves.
            if (Pointer is ISynchronousPointer synchronousPointer)
                synchronousPointer.Flush();
        }

        // adds delta*sensitivity to acc, returns whole ticks, keeps fractional remainder
        public static int AccumulateScroll(ref float acc, float delta, float sensitivity)
        {
            acc += delta * sensitivity;
            int ticks = (int)acc;
            acc -= ticks;
            return ticks;
        }

        private static IEnumerable<string>? validDirections;
        public static IEnumerable<string> ValidDirections =>
            validDirections ??= Enum.GetNames<ScrollAxis>();

        public override string ToString() =>
            $"{PLUGIN_NAME}: Direction: {Direction}, Sensitivity: {Sensitivity}, Natural: {Natural}";
    }
}
