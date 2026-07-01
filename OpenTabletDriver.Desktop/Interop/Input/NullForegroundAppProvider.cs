using System;
using System.Collections.Generic;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    // TODO(app-specific-bindings): Windows/Linux foreground-app detection is not
    // implemented — this stub always reports "no app", so app-specific bindings fall
    // back to the "All Applications" profile everywhere on those platforms. Per-app
    // switching only works on macOS today (see MacOSForegroundAppProvider).
    //
    // To finish the other platforms, implement a real IForegroundAppProvider for each
    // and wire it into DesktopInterop.ForegroundApp:
    //   - Windows: GetForegroundWindow -> GetWindowThreadProcessId -> match by process
    //     exe name (Wacom keys on the executable, so use the .exe filename as the match
    //     key, mirroring what bundle id is on macOS).
    //   - Linux: X11 _NET_ACTIVE_WINDOW + WM_CLASS, or the Wayland-compositor equivalent.
    //     The match key should be the WM_CLASS / app id.
    //
    // IMPORTANT LESSON (macOS): do NOT use a "give me the frontmost app" API that can be
    // cached/stale in a background process (NSWorkspace.frontmostApplication latched to
    // the app that was focused when the daemon launched). Read the *live* focused window
    // at query time. The match key stored per-app lives in AppBindingProfile.BundleIdentifier
    // and the picker fills it in ApplicationBindingSelector / AddApplicationDialog — so
    // whatever key a platform's detection returns must match what its picker stores.
    internal sealed class NullForegroundAppProvider : IForegroundAppProvider
    {
        public bool TryGetForegroundApp(out string bundleId, out string displayName)
        {
            bundleId = string.Empty;
            displayName = string.Empty;
            return false;
        }

        public IEnumerable<(string BundleId, string DisplayName)> GetRunningApplications() =>
            Array.Empty<(string, string)>();

        public bool TryGetBundleInfo(string path, out string bundleId, out string displayName)
        {
            bundleId = string.Empty;
            displayName = string.Empty;
            return false;
        }
    }
}
