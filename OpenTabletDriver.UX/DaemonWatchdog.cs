using System;
using System.Diagnostics;
using System.IO;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX
{
    public sealed class DaemonWatchdog : IDisposable
    {
        public event EventHandler? DaemonExited;

        private Process? daemonProcess;

        private readonly static ProcessStartInfo startInfo = SystemInterop.CurrentPlatform switch
        {
            PluginPlatform.Windows => new ProcessStartInfo
            {
                FileName = Path.Join(AppContext.BaseDirectory, "OpenTabletDriver.Daemon.exe"),
                Arguments = "",
                WorkingDirectory = AppContext.BaseDirectory,
                CreateNoWindow = true
            },
            PluginPlatform.MacOS => new ProcessStartInfo
            {
                FileName = FindMacOSDaemon()
            },
            _ => new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = Path.Join(AppContext.BaseDirectory, "OpenTabletDriver.Daemon.dll")
            }
        };

        public static bool CanExecute =>
            File.Exists(startInfo.FileName) ||
            File.Exists(startInfo.Arguments);

        // In the .NET-for-macOS app bundle the managed assemblies (and thus BaseDirectory) live in
        // Contents/MonoBundle, while the daemon executable sits next to the native launcher in
        // Contents/MacOS - probe both so the watchdog works in the bundle and in flat dev layouts.
        private static string FindMacOSDaemon()
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Join(baseDir, "OpenTabletDriver.Daemon"),
                Path.GetFullPath(Path.Join(baseDir, "..", "MacOS", "OpenTabletDriver.Daemon"))
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return candidates[0];
        }

        public void Start()
        {
            this.daemonProcess = new Process()
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            this.daemonProcess.Exited += (_, e) =>
            {
                DaemonExited?.Invoke(this, EventArgs.Empty);
            };
            this.daemonProcess.Start();
        }

        public void Stop()
        {
            DaemonExited = null; // unregister all event handlers
            this.daemonProcess?.Kill();
            this.daemonProcess?.Dispose();
            this.daemonProcess = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
