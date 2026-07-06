using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    /// <summary>
    /// Cycles the absolute output area through each connected display (fullscreen), optionally
    /// adding an "all displays" step. Updates settings so the pen remaps and the UI follows.
    /// </summary>
    [PluginName("Display Swap")]
    public class DisplaySwapBinding : IStateBinding
    {
        [Resolved]
        public IDriverDaemon? Daemon { set; get; }

        [BooleanProperty("Cycle Through All Displays", "Add an all-displays step to the cycle."),
         DefaultPropertyValue(false)]
        public bool IncludeAllDisplays { set; get; }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (Daemon == null)
                return;

            var screen = DesktopInterop.VirtualScreen;
            var displays = screen?.Displays.ToList();
            if (screen == null || displays == null || displays.Count == 0)
                return;

            var targets = displays
                .Select(d => new Area(d.Width, d.Height,
                    new Vector2(d.Position.X + (d.Width / 2f), d.Position.Y + (d.Height / 2f))))
                .ToList();

            if (IncludeAllDisplays && displays.Count > 1)
                targets.Add(new Area(screen.Width, screen.Height,
                    new Vector2(screen.Width / 2f, screen.Height / 2f)));

            var settings = Daemon.GetSettings().GetAwaiter().GetResult();
            var profile = settings.Profiles[tablet];
            if (profile?.AbsoluteModeSettings == null)
                return;

            // Stateless: SetSettings rebuilds this binding each press, so derive the current step
            // from the saved area instead of an instance counter, then advance to the next.
            var current = profile.AbsoluteModeSettings.Display;
            var currentIndex = ClosestTarget(targets, current);
            var next = (currentIndex + 1) % targets.Count;
            profile.AbsoluteModeSettings.Display.Area = targets[next];

            Daemon.SetSettings(settings);
            Daemon.ForceResynchronize();

            // "all displays" is the last step when included; otherwise next maps to a monitor.
            var selection = (IncludeAllDisplays && displays.Count > 1 && next == targets.Count - 1)
                ? "all"
                : next.ToString(CultureInfo.InvariantCulture);
            ShowOverlay(displays, selection);
        }

        private const string OverlayHelper = "OpenTabletDriver.DisplaySwapOverlay";

        private static void ShowOverlay(IReadOnlyList<IDisplay> displays, string selection)
        {
            if (SystemInterop.CurrentPlatform != PluginPlatform.MacOS)
            {
                // No native helper on this platform; the GUI draws the HUD from this event.
                OverlayHub.Publish("display-swap", new
                {
                    Selection = selection,
                    Displays = displays.Select(d => new
                    {
                        X = d.Position.X,
                        Y = d.Position.Y,
                        W = d.Width,
                        H = d.Height
                    }).ToArray()
                });
                return;
            }

            var helperPath = Path.Combine(AppContext.BaseDirectory, OverlayHelper);
            if (!File.Exists(helperPath))
                return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = helperPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(selection);
                foreach (var d in displays)
                {
                    psi.ArgumentList.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3}", d.Position.X, d.Position.Y, d.Width, d.Height));
                }
                Process.Start(psi)?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("Display Swap", $"Unable to show display swap overlay: {ex.Message}", LogLevel.Debug);
            }
        }

        private static int ClosestTarget(IReadOnlyList<Area> targets, AreaSettings current)
        {
            var best = 0;
            var bestDist = float.MaxValue;
            for (var i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                var dist = MathF.Abs(t.Position.X - current.X) + MathF.Abs(t.Position.Y - current.Y)
                         + MathF.Abs(t.Width - current.Width) + MathF.Abs(t.Height - current.Height);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }
    }
}
