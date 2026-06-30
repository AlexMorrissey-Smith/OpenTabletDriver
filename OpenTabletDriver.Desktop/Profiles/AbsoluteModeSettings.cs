using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Profiles
{
    public class AbsoluteModeSettings : ViewModel
    {
        private bool _lockar, _clipping, _areaLimiting;

        [JsonProperty(nameof(Display))]
        public required AreaSettings Display
        {
            get;
            set => this.RaiseAndSetIfChanged(ref field, value);
        }

        [JsonProperty(nameof(Tablet))]
        public required AreaSettings Tablet
        {
            get;
            set => this.RaiseAndSetIfChanged(ref field, value);
        }

        /// <summary>
        /// Remembered <see cref="Display"/> areas keyed by monitor-layout signature, so the
        /// output area auto-adapts when displays are connected/disconnected.
        /// </summary>
        [JsonProperty(nameof(DisplayLayouts))]
        public Dictionary<string, AreaSettings> DisplayLayouts { get; set; } = new();

        [JsonProperty(nameof(EnableClipping))]
        public bool EnableClipping
        {
            set => RaiseAndSetIfChanged(ref _clipping, value);
            get => _clipping;
        }

        [JsonProperty(nameof(EnableAreaLimiting))]
        public bool EnableAreaLimiting
        {
            set => RaiseAndSetIfChanged(ref _areaLimiting, value);
            get => _areaLimiting;
        }

        [JsonProperty(nameof(LockAspectRatio))]
        public bool LockAspectRatio
        {
            set => RaiseAndSetIfChanged(ref _lockar, value);
            get => _lockar;
        }

        public static AbsoluteModeSettings GetDefaults(DigitizerSpecifications digitizer)
        {
            var display = AppInfo.PluginManager.GetService<IVirtualScreen>() ?? throw new InvalidOperationException($"Could not get {nameof(IVirtualScreen)} from DI");

            return new AbsoluteModeSettings
            {
                Display = AreaSettings.GetDefaults(display),
                Tablet = AreaSettings.GetDefaults(digitizer),
                EnableClipping = true
            };
        }
    }
}
