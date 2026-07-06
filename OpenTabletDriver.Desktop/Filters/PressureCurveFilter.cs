using System;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

#nullable enable

namespace OpenTabletDriver.Desktop.Filters
{
    /// <summary>
    /// Remaps pen pressure through a cubic bézier from (0,0) to (1,1) with two
    /// user controls — the "pressure curve" every vendor driver ships. The GUI
    /// edits the control points graphically; presets are just point values.
    /// </summary>
    [PluginName("Pressure Curve")]
    public class PressureCurveFilter : IPositionedPipelineElement<IDeviceReport>
    {
        [Property("Control Point 1 X"), DefaultPropertyValue(0.33f)]
        public float X1 { set; get; } = 0.33f;

        [Property("Control Point 1 Y"), DefaultPropertyValue(0.33f)]
        public float Y1 { set; get; } = 0.33f;

        [Property("Control Point 2 X"), DefaultPropertyValue(0.67f)]
        public float X2 { set; get; } = 0.67f;

        [Property("Control Point 2 Y"), DefaultPropertyValue(0.67f)]
        public float Y2 { set; get; } = 0.67f;

        [TabletReference]
        public TabletReference? Tablet { set; get; }

        public PipelinePosition Position => PipelinePosition.PostTransform;

        public event Action<IDeviceReport?>? Emit;

        public void Consume(IDeviceReport? report)
        {
            if (report is ITabletReport tabletReport)
            {
                var maxPressure = Tablet?.Properties?.Specifications?.Pen?.MaxPressure ?? 0;
                if (maxPressure > 0 && tabletReport.Pressure > 0)
                {
                    var normalized = Math.Clamp(tabletReport.Pressure / (float)maxPressure, 0f, 1f);
                    var curved = Math.Clamp(EvaluateBezier(normalized), 0f, 1f);
                    tabletReport.Pressure = (uint)MathF.Round(curved * maxPressure);
                }
            }

            Emit?.Invoke(report);
        }

        /// <summary>
        /// y for a given x on the bézier (0,0) P1 P2 (1,1) — same solve as CSS
        /// cubic-bezier: Newton on x(t), bisection fallback, then evaluate y(t).
        /// </summary>
        private float EvaluateBezier(float x)
        {
            var t = x; // good initial guess: curve is monotonic-ish in x
            for (var i = 0; i < 8; i++)
            {
                var error = SampleAxis(t, X1, X2) - x;
                if (MathF.Abs(error) < 1e-5f)
                    return SampleAxis(t, Y1, Y2);

                var slope = SampleAxisDerivative(t, X1, X2);
                if (MathF.Abs(slope) < 1e-6f)
                    break;
                t -= error / slope;
            }

            // Newton didn't converge (extreme control points): bisect.
            float lo = 0f, hi = 1f;
            t = x;
            for (var i = 0; i < 20; i++)
            {
                if (SampleAxis(t, X1, X2) < x) lo = t;
                else hi = t;
                t = (lo + hi) / 2f;
            }
            return SampleAxis(t, Y1, Y2);
        }

        private static float SampleAxis(float t, float p1, float p2)
        {
            var u = 1f - t;
            return 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t;
        }

        private static float SampleAxisDerivative(float t, float p1, float p2)
        {
            var u = 1f - t;
            return 3f * u * u * p1 + 6f * u * t * (p2 - p1) + 3f * t * t * (1f - p2);
        }
    }
}
