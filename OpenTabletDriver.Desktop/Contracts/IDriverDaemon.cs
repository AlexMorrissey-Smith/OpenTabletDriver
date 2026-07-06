using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Diagnostics;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Desktop.Updater;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Contracts
{
    public interface IDriverDaemon
    {
        event EventHandler<LogMessage>? Message;
        event EventHandler<DebugReportData>? DeviceReport;
        event EventHandler<IEnumerable<TabletReference>>? TabletsChanged;
        event EventHandler? Resynchronize;
        event EventHandler<OverlayRequest>? Overlay;

        Task WriteMessage(LogMessage message);

        Task LoadPlugins();
        Task<bool> InstallPlugin(string filePath);
        Task<bool> UninstallPlugin(string friendlyName);
        Task<bool> DownloadPlugin(PluginMetadata metadata);

        // Additive read-only metadata the webview frontend can't get via reflection.
        Task<PluginTypeCatalog> GetPluginTypes();
        Task<VirtualScreenInfo> GetVirtualScreen();
        Task<IEnumerable<PluginMetadata>> GetLoadedPlugins();
        Task<IEnumerable<PluginMetadata>> GetPluginMetadataRepository();

        // Presets (were handled in-process by the old GUI's PresetManager).
        Task<IEnumerable<string>> GetPresets();
        Task SavePreset(string name, Settings settings);
        Task ApplyPreset(string name);

        Task<IEnumerable<SerializedDeviceEndpoint>> GetDevices();

        Task<IEnumerable<TabletReference>> GetTablets();
        Task<IEnumerable<TabletReference>> DetectTablets();

        Task SetSettings(Settings settings);
        Task<Settings> GetSettings();
        Task ResetSettings();

        Task<AppInfo> GetApplicationInfo();

        Task SetTabletDebug(bool isEnabled);
        Task<string> RequestDeviceString(int vendorID, int productID, int index);

        Task<IEnumerable<LogMessage>> GetCurrentLog();
        Task<DiagnosticInfo> GetDiagnosticInfo();

        Task<SerializedUpdateInfo?> CheckForUpdates();
        Task InstallUpdate();

        Task ForceResynchronize();
    }
}
