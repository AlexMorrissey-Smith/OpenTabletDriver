using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

#nullable enable

namespace OpenTabletDriver.Desktop.Binding
{
    public enum WheelMode
    {
        Scroll,
        BrushSize,
        Zoom
    }

    internal static class WheelModeState
    {
        private static int currentMode = (int)WheelMode.Scroll;
        private static int currentVersion;

        public static WheelMode Current => (WheelMode)Volatile.Read(ref currentMode);
        public static int Version => Volatile.Read(ref currentVersion);

        public static WheelMode Next()
        {
            while (true)
            {
                var current = Volatile.Read(ref currentMode);
                var next = (current + 1) % Enum.GetValues<WheelMode>().Length;
                if (Interlocked.CompareExchange(ref currentMode, next, current) == current)
                {
                    Interlocked.Increment(ref currentVersion);
                    return (WheelMode)next;
                }
            }
        }

        public static void Reset()
        {
            Volatile.Write(ref currentMode, (int)WheelMode.Scroll);
            Interlocked.Increment(ref currentVersion);
        }
    }

    [PluginName("Wheel Mode Switch")]
    public sealed class WheelModeSwitchBinding : IStateBinding
    {
        public static void ResetMode() => WheelModeState.Reset();

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            var mode = WheelModeState.Next();
            WheelModeOverlay.Show(mode);
            Log.Write("Wheel Mode", $"Wheel mode: {Format(mode)}", LogLevel.Debug);
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }

        public override string ToString() => $"Wheel Mode Switch: {Format(WheelModeState.Current)}";

        internal static string Format(WheelMode mode) => mode switch
        {
            WheelMode.Scroll => "Scroll",
            WheelMode.BrushSize => "Brush Size",
            WheelMode.Zoom => "Zoom",
            _ => mode.ToString()
        };
    }

    public abstract class WheelModeActionBinding : IStateBinding
    {
        private int _scrollAmount = 12;
        private int _brushDetentsPerStep = 1;
        private int _zoomDetentsPerStep = 2;
        private int _debounceMs = 25;
        private int _brushDetents;
        private int _zoomDetents;
        private int _seenModeVersion = WheelModeState.Version;
        private long _lastAcceptedTimestamp;
        private static readonly string[] ZoomInMac = ["Application", "Equal"];
        private static readonly string[] ZoomOutMac = ["Application", "Minus"];
        private static readonly string[] ZoomInDefault = ["Control", "Equal"];
        private static readonly string[] ZoomOutDefault = ["Control", "Minus"];

        [Resolved]
        public IMouseScrollHandler? Pointer { set; get; }

        [Resolved]
        public IVirtualKeyboard? Keyboard { set; get; }

        protected abstract bool IsClockwise { get; }

        [Property("Scroll Amount"),
         DefaultPropertyValue(12),
         ToolTip("Mouse wheel pixels per accepted wheel detent.")]
        public int ScrollAmount
        {
            get => _scrollAmount;
            set => _scrollAmount = Math.Max(1, value);
        }

        [Property("Brush Detents Per Step"),
         DefaultPropertyValue(1),
         ToolTip("Accepted wheel detents required before one brush-size key press.")]
        public int BrushDetentsPerStep
        {
            get => _brushDetentsPerStep;
            set => _brushDetentsPerStep = Math.Max(1, value);
        }

        [Property("Zoom Detents Per Step"),
         DefaultPropertyValue(2),
         ToolTip("Accepted wheel detents required before one zoom key press.")]
        public int ZoomDetentsPerStep
        {
            get => _zoomDetentsPerStep;
            set => _zoomDetentsPerStep = Math.Max(1, value);
        }

        [Property("Debounce"),
         DefaultPropertyValue(25),
         Unit("ms"),
         ToolTip("Ignore duplicate wheel reports arriving too close together.")]
        public int DebounceMs
        {
            get => _debounceMs;
            set => _debounceMs = Math.Max(0, value);
        }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            ResetDetentsAfterModeChange();
            if (!AcceptDebounced())
                return;

            switch (WheelModeState.Current)
            {
                case WheelMode.Scroll:
                    Scroll();
                    break;
                case WheelMode.BrushSize:
                    if (AdvanceDetent(ref _brushDetents, BrushDetentsPerStep))
                        TapKey(IsClockwise ? "RightBracket" : "LeftBracket");
                    break;
                case WheelMode.Zoom:
                    if (AdvanceDetent(ref _zoomDetents, ZoomDetentsPerStep))
                        TapKeys(IsClockwise ? ZoomInChord() : ZoomOutChord());
                    break;
            }
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }

        public override string ToString() => $"{GetType().GetCustomAttributes(typeof(PluginNameAttribute), true).OfType<PluginNameAttribute>().FirstOrDefault()?.Name}: {WheelModeSwitchBinding.Format(WheelModeState.Current)}";

        private void Scroll()
        {
            if (Pointer == null)
                return;

            Pointer.ScrollVertically(IsClockwise ? -ScrollAmount : ScrollAmount);
            if (Pointer is ISynchronousPointer synchronousPointer)
                synchronousPointer.Flush();
        }

        private void ResetDetentsAfterModeChange()
        {
            var version = WheelModeState.Version;
            if (_seenModeVersion == version)
                return;

            _brushDetents = 0;
            _zoomDetents = 0;
            _lastAcceptedTimestamp = 0;
            _seenModeVersion = version;
        }

        private bool AcceptDebounced()
        {
            var now = Stopwatch.GetTimestamp();
            if (_lastAcceptedTimestamp != 0 && ElapsedMilliseconds(_lastAcceptedTimestamp, now) < DebounceMs)
                return false;

            _lastAcceptedTimestamp = now;
            return true;
        }

        private static double ElapsedMilliseconds(long startTimestamp, long endTimestamp) =>
            (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;

        private static bool AdvanceDetent(ref int detents, int detentsPerStep)
        {
            detents++;
            if (detents < detentsPerStep)
                return false;

            detents = 0;
            return true;
        }

        private void TapKey(string key)
        {
            if (!IsSupported(key))
                return;

            Keyboard!.Press(key);
            Keyboard.Release(key);
        }

        private void TapKeys(IReadOnlyList<string> keys)
        {
            if (Keyboard == null || keys.Any(key => !Keyboard.SupportedKeys.Contains(key)))
                return;

            Keyboard.Press(keys);
            Keyboard.Release(keys.Reverse());
        }

        private bool IsSupported(string key) =>
            Keyboard != null && Keyboard.SupportedKeys.Contains(key);

        private static string[] ZoomInChord() =>
            SystemInterop.CurrentPlatform == PluginPlatform.MacOS ? ZoomInMac : ZoomInDefault;

        private static string[] ZoomOutChord() =>
            SystemInterop.CurrentPlatform == PluginPlatform.MacOS ? ZoomOutMac : ZoomOutDefault;
    }

    [PluginName("Wheel Mode Clockwise Action")]
    public sealed class WheelModeClockwiseBinding : WheelModeActionBinding
    {
        protected override bool IsClockwise => true;
    }

    [PluginName("Wheel Mode Counter-Clockwise Action")]
    public sealed class WheelModeCounterClockwiseBinding : WheelModeActionBinding
    {
        protected override bool IsClockwise => false;
    }

    internal static class WheelModeOverlay
    {
        private const string HelperName = "OpenTabletDriver.WheelModeOverlay";

        public static void Show(WheelMode mode)
        {
            if (SystemInterop.CurrentPlatform != PluginPlatform.MacOS)
                return;

            var helperPath = Path.Combine(AppContext.BaseDirectory, HelperName);
            if (!File.Exists(helperPath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = helperPath,
                    ArgumentList = { WheelModeSwitchBinding.Format(mode) },
                    UseShellExecute = false,
                    CreateNoWindow = true
                })?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("Wheel Mode", $"Unable to show wheel mode overlay: {ex.Message}", LogLevel.Debug);
            }
        }
    }
}
