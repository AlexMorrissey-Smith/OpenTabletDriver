using System.Diagnostics;
using System.Windows;

namespace OpenTabletDriver.UX.Wpf
{
    // Windows has no free "follow the OS theme" story the way macOS/GTK do for native controls -
    // Eto.Platform.Wpf renders plain WPF, which stays light-only unless something opts in.
    // .NET 9+ WPF ships a built-in Fluent theme with ThemeMode = System, so try that first; it's
    // one property instead of hand-rolling registry polling + resource dictionaries + a DWM
    // dark-titlebar P/Invoke. If Eto.Wpf's own control styles turn out to fight the Fluent theme
    // (unverified without a Windows build), that's the trigger to build the manual fallback -
    // don't build it speculatively.
    internal static class WindowsTheme
    {
        public static void TryApplySystemTheme()
        {
            try
            {
                // ThemeMode is still marked experimental (WPF0001) upstream as of .NET 10, but it's
                // the real shipping Fluent light/dark/accent implementation - not a preview stub.
#pragma warning disable WPF0001
                if (Application.Current is { } app)
                    app.ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"WindowsTheme: couldn't set ThemeMode.System, falling back to default WPF theme: {ex}");
            }
        }
    }
}
