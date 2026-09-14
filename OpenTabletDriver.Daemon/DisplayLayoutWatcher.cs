using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.Daemon
{
    /// <summary>
    /// Remembers a separate absolute-mode Display (output) area per monitor layout and swaps to
    /// the matching one when displays are connected or disconnected, so the mapping auto-adapts
    /// instead of pointing at a screen that is no longer there.
    /// Ported from the old Eto GUI's watcher into the daemon so it works regardless of which GUI
    /// (or none) is running. The daemon has no OS screen-change event source, so it polls; the
    /// virtual-screen re-read is a couple of cheap CG/WinAPI calls.
    /// </summary>
    internal sealed class DisplayLayoutWatcher : IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

        private readonly Func<Settings?> _getSettings;
        private readonly Func<Settings, Task> _apply;
        private readonly Timer _timer;
        private string? _lastSignature;
        private bool _applyDirty;
        private int _busy;

        public DisplayLayoutWatcher(Func<Settings?> getSettings, Func<Settings, Task> apply)
        {
            _getSettings = getSettings;
            _apply = apply;
            _timer = new Timer(_ => Tick(), null, PollInterval, PollInterval);
        }

        public void Dispose() => _timer.Dispose();

        private async void Tick()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return;
            try
            {
                var settings = _getSettings();
                if (settings == null)
                    return;

                var signature = CurrentSignature();
                if (signature == null)
                    return;

                if (_lastSignature == null)
                {
                    // Seed the current layout. Settings written before this watcher
                    // existed can carry an area from a layout that is long gone
                    // (e.g. spanning two monitors while one is connected): heal
                    // those by resetting to the full current screen.
                    var screen = DesktopInterop.VirtualScreen;
                    foreach (var abs in AbsoluteSettings(settings))
                    {
                        if (abs.Display == null)
                            continue;
                        if (screen != null && OutOfBounds(abs.Display, screen.Width, screen.Height))
                        {
                            Log.Write("Display", "Stored output area lies outside the current monitor layout; resetting to full screen");
                            abs.Display = AreaSettings.GetDefaults(screen);
                            _applyDirty = true;
                        }
                        abs.DisplayLayouts.TryAdd(signature, Clone(abs.Display));
                    }
                    _lastSignature = signature;
                }
                else if (signature != _lastSignature)
                {
                    Log.Write("Display", $"Monitor layout changed → {signature.TrimEnd('|')}; adapting output areas");
                    AdaptToNewLayout(settings, _lastSignature, signature);
                    _lastSignature = signature;
                    _applyDirty = true;
                }

                // Apply is separate from adapt so a transient failure retries the send next tick
                // without re-running the (non-idempotent) area swap.
                if (_applyDirty)
                {
                    await _apply(settings);
                    _applyDirty = false;
                }
            }
            catch (Exception e)
            {
                Log.Exception(e); // _applyDirty stays set on apply failure → retried next tick
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private static void AdaptToNewLayout(Settings settings, string oldSignature, string newSignature)
        {
            var screen = DesktopInterop.VirtualScreen;

            foreach (var abs in AbsoluteSettings(settings))
            {
                if (abs.Display == null)
                    continue;

                // Stash the area that was in use for the layout we are leaving.
                abs.DisplayLayouts[oldSignature] = Clone(abs.Display);

                if (abs.DisplayLayouts.TryGetValue(newSignature, out var saved))
                {
                    abs.Display = Clone(saved);
                }
                else
                {
                    // Never-seen layout: map to the full available screen, then remember it.
                    var fallback = screen != null ? AreaSettings.GetDefaults(screen) : Clone(abs.Display);
                    abs.Display = fallback;
                    abs.DisplayLayouts[newSignature] = Clone(fallback);
                }
            }
        }

        // X/Y are the area center. 1px slack for rounding.
        private static bool OutOfBounds(AreaSettings a, float screenWidth, float screenHeight) =>
            a.X - a.Width / 2 < -1 || a.Y - a.Height / 2 < -1 ||
            a.X + a.Width / 2 > screenWidth + 1 || a.Y + a.Height / 2 > screenHeight + 1;

        private static IEnumerable<AbsoluteModeSettings> AbsoluteSettings(Settings settings) =>
            settings.Profiles
                .Select(profile => profile.AbsoluteModeSettings)
                .Where(abs => abs != null)!;

        private static AreaSettings Clone(AreaSettings area) => new()
        {
            Width = area.Width,
            Height = area.Height,
            X = area.X,
            Y = area.Y,
            Rotation = area.Rotation
        };

        private static string? CurrentSignature()
        {
            DesktopInterop.RefreshVirtualScreen();
            var displays = DesktopInterop.VirtualScreen?.Displays;
            if (displays == null)
                return null;

            var sb = new StringBuilder();
            foreach (var d in displays
                         .OrderBy(d => d.Position.Y)
                         .ThenBy(d => d.Position.X)
                         .ThenBy(d => d.Width)
                         .ThenBy(d => d.Height))
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0}x{1:0}@{2:0},{3:0}|",
                    d.Width, d.Height, d.Position.X, d.Position.Y));
            }

            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
