using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Eto.Forms;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletDriver.UX
{
    /// <summary>
    /// Remembers a separate absolute-mode Display (output) area per monitor layout and swaps to
    /// the matching one when displays are connected or disconnected, so the mapping auto-adapts
    /// instead of pointing at a screen that is no longer there.
    /// Driven by the OS screen-change event (instant) with a slow poll as a safety net.
    /// </summary>
    public sealed class DisplayLayoutWatcher : IDisposable
    {
        private readonly UITimer timer;
        private readonly UITimer debounce;
        private readonly Func<Settings, Task> apply;
        private string? lastSignature;
        private bool applyDirty;
        private bool applying;

        public DisplayLayoutWatcher(Func<Settings, Task> apply)
        {
            this.apply = apply;
            // ponytail: 5s poll is just a backstop; the real trigger is DisplaysChanged.
            timer = new UITimer { Interval = 5.0 };
            timer.Elapsed += (_, _) => Tick();

            // Coalesce the burst of screen-change events macOS fires during reconfiguration so we
            // act once on the settled layout, not on transient intermediate states.
            debounce = new UITimer { Interval = 0.6 };
            debounce.Elapsed += (_, _) => { debounce.Stop(); Tick(); };
        }

        public void Start()
        {
            DesktopInterop.DisplaysChanged += OnDisplaysChanged;
            Tick(); // seed the current layout; changes nothing
            timer.Start();
        }

        public void Dispose()
        {
            DesktopInterop.DisplaysChanged -= OnDisplaysChanged;
            timer.Stop();
            timer.Dispose();
            debounce.Stop();
            debounce.Dispose();
        }

        private void OnDisplaysChanged() => Application.Instance.AsyncInvoke(() =>
        {
            debounce.Stop();
            debounce.Start();
        });

        private void Tick()
        {
            if (App.Current.Settings is not { } settings)
                return;

            var signature = CurrentSignature();
            if (signature == null)
                return;

            if (lastSignature == null)
            {
                foreach (var abs in AbsoluteSettings(settings))
                    abs.DisplayLayouts.TryAdd(signature, Clone(abs.Display));
                lastSignature = signature;
            }
            else if (signature != lastSignature)
            {
                AdaptToNewLayout(settings, lastSignature, signature);
                lastSignature = signature;
                applyDirty = true;
            }

            if (applyDirty)
                FlushApply(settings);
        }

        // Apply is separated from adapt so a transient daemon hiccup retries the send on the next
        // tick without re-running the (non-idempotent) area swap.
        private void FlushApply(Settings settings)
        {
            if (applying)
                return;
            applying = true;

            // ReSharper disable once AsyncVoidMethod
            Application.Instance.AsyncInvoke(async void () =>
            {
                try
                {
                    await apply(settings);
                    applyDirty = false;
                }
                catch
                {
                    // leave applyDirty set; next tick retries
                }
                finally
                {
                    applying = false;
                }
            });
        }

        private static void AdaptToNewLayout(Settings settings, string oldSignature, string newSignature)
        {
            var screen = DesktopInterop.VirtualScreen;

            foreach (var abs in AbsoluteSettings(settings))
            {
                // Stash the area that was in use for the layout we are leaving.
                abs.DisplayLayouts[oldSignature] = Clone(abs.Display);

                if (abs.DisplayLayouts.TryGetValue(newSignature, out var saved))
                {
                    abs.Display = Clone(saved);
                }
                else
                {
                    // Never-seen layout: map to the full available screen so it adapts, then remember it.
                    var fallback = screen != null ? AreaSettings.GetDefaults(screen) : Clone(abs.Display);
                    abs.Display = fallback;
                    abs.DisplayLayouts[newSignature] = Clone(fallback);
                }
            }
        }

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
