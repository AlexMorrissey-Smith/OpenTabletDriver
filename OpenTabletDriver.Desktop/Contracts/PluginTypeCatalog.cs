using System.Collections.Generic;

namespace OpenTabletDriver.Desktop.Contracts
{
    // Serializable description of the installed plugin types + their settings schema.
    // The old Eto GUI discovered these via in-process reflection; a webview frontend
    // can't, so the daemon exposes them (see GetPluginTypes on IDriverDaemon).

    public class SerializedPluginSetting
    {
        public string Property { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        /// <summary>"bool" | "number" | "string" | "enum".</summary>
        public string Kind { get; set; } = "string";
        public object? Default { get; set; }
        public string[]? EnumValues { get; set; }
        public float? Min { get; set; }
        public float? Max { get; set; }
        public string? ToolTip { get; set; }
        public string? Unit { get; set; }
    }

    public class SerializedPluginType
    {
        /// <summary>Full type name (the value stored in a PluginSettingStore.Path).</summary>
        public string Path { get; set; } = string.Empty;
        /// <summary>Friendly display name (PluginNameAttribute) or the short type name.</summary>
        public string Name { get; set; } = string.Empty;
        public List<SerializedPluginSetting> Settings { get; set; } = new();
    }

    public class PluginTypeCatalog
    {
        public List<SerializedPluginType> Bindings { get; set; } = new();
        public List<SerializedPluginType> OutputModes { get; set; } = new();
        public List<SerializedPluginType> Filters { get; set; } = new();
        public List<SerializedPluginType> Tools { get; set; } = new();
    }
}
