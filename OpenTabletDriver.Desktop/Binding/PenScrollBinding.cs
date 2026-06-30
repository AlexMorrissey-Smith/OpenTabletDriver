using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timers;

#nullable enable

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class PenScrollBinding : IReportBinding
    {
        private const string PLUGIN_NAME = "Pen Scroll";
        private const float GlideIntervalMs = 15f;     // ~66 Hz coast
        private const float MinGlideSpeed = 0.05f;      // scroll units/ms below which coast idles
        private const float MaxGlideSpeed = 1.2f;       // cap so a hard flick isn't violent
        // Velocity decays as rate^milliseconds (Apple's model); 0.995/ms ≈ a short, natural tail.
        private static readonly float Friction = MathF.Pow(0.995f, GlideIntervalMs);

        private enum ScrollAxis { Both, Vertical, Horizontal }

        private ScrollAxis _axis = ScrollAxis.Both;
        private bool _held;
        private bool _wasTouching;
        private Vector2 _lastReal;
        private float _accX, _accY;     // live scroll remainder
        private float _gAccX, _gAccY;   // glide scroll remainder
        private Vector2 _vel;           // smoothed velocity, scroll units/ms
        private Vector2 _momentum;      // active coast velocity, scroll units/ms
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        [Resolved]
        public IMouseScrollHandler? Pointer { set; get; }

        private ITimer? _timer;

        [Resolved]
        public ITimer? Timer
        {
            get => _timer;
            set
            {
                if (_timer != null)
                    _timer.Elapsed -= Glide;
                _timer = value;
                if (_timer != null)
                {
                    _timer.Interval = GlideIntervalMs;
                    _timer.Elapsed += Glide;
                }
            }
        }

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
         DefaultPropertyValue(2f),
         ToolTip("Scroll units emitted per pixel of pen movement. Higher = faster scroll.\n\n" +
                 "Tuned for macOS (pixel-unit scroll). On Windows/Linux a tick is 120, " +
                 "so you may want a much larger value there.")]
        // ponytail: single default; pixel units on macOS, 120=notch on Win/Linux — bump per platform if it feels off
        public float Sensitivity { get; set; } = 2f;

        [Property("Natural"),
         DefaultPropertyValue(true),
         ToolTip("Natural scrolling (like Apple trackpads): content follows the pen. " +
                 "Turn off for traditional scrolling.")]
        public bool Natural { get; set; } = true;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (report is not IAbsolutePositionReport posReport)
                return;
            StopGlide();
            _lastReal = posReport.Position;
            _accX = _accY = 0;
            _vel = Vector2.Zero;
            _wasTouching = false;
            _watch.Restart();
            _held = true;
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            _held = false;
            StopGlide(); // button = scroll mode; releasing it ends the coast (report thread, safe)
        }

        public void OnReport(TabletReference tablet, IDeviceReport report)
        {
            if (!_held || report is not IAbsolutePositionReport posReport)
                return;

            var real = posReport.Position;
            var delta = real - _lastReal;
            _lastReal = real; // always track so resuming contact doesn't jump

            // Contact-only (Wacom-style): scroll only while the tip is touching. BindingHandler
            // restores the real pressure for us (the tip threshold otherwise zeroes it).
            if (report is not ITabletReport { Pressure: > 0 })
            {
                if (_wasTouching) StartGlide(); // lifted off surface → coast
                _wasTouching = false;
                return;
            }
            if (!_wasTouching) StopGlide();      // new contact cancels any coast
            _wasTouching = true;

            // Natural: content follows the pen (pen down → scroll up); horizontal is mirrored.
            float sign = Natural ? 1f : -1f;
            float vy = _axis != ScrollAxis.Horizontal ? sign * delta.Y * Sensitivity : 0f;
            float vx = _axis != ScrollAxis.Vertical ? -sign * delta.X * Sensitivity : 0f;

            Emit(ref _accY, vy, vertical: true);
            Emit(ref _accX, vx, vertical: false);

            // smoothed velocity (scroll units/ms) feeds inertia on lift
            float dt = Math.Clamp((float)_watch.Elapsed.TotalMilliseconds, 1f, 50f);
            _watch.Restart();
            _vel = Vector2.Lerp(_vel, new Vector2(vx, vy) / dt, 0.4f);

            Flush();
        }

        // Called on lift (report thread). Starts the coast timer; Stop only ever happens on the
        // report thread (Press/Release/new-contact) — never from Glide, which would self-Join the
        // timer thread under its stateLock and deadlock the whole pipeline.
        private void StartGlide()
        {
            float speed = _vel.Length();
            if (speed < MinGlideSpeed)
                return;
            _momentum = speed > MaxGlideSpeed ? _vel * (MaxGlideSpeed / speed) : _vel;
            _gAccX = _gAccY = 0;
            Timer?.Start();
        }

        // Runs on the timer thread. Pointer/_momentum are shared with the report thread;
        // MacOSVirtualMouse is now internally locked, so emission is safe.
        private void Glide()
        {
            if (_momentum.Length() < MinGlideSpeed)
            {
                _momentum = Vector2.Zero; // coast spent; idles until Release/new-contact stops it
                return;
            }

            var step = _momentum * GlideIntervalMs; // units this tick
            Emit(ref _gAccY, step.Y, vertical: true);
            Emit(ref _gAccX, step.X, vertical: false);
            Flush();

            _momentum *= Friction;
        }

        private void StopGlide()
        {
            _momentum = Vector2.Zero;
            Timer?.Stop();
        }

        private void Emit(ref float acc, float amount, bool vertical)
        {
            int ticks = AccumulateScroll(ref acc, amount, 1f); // amount already signed + scaled
            if (ticks == 0)
                return;
            if (vertical)
                Pointer?.ScrollVertically(ticks);
            else
                Pointer?.ScrollHorizontally(ticks);
        }

        private void Flush()
        {
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
