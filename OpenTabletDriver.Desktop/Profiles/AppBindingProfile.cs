using Newtonsoft.Json;

namespace OpenTabletDriver.Desktop.Profiles
{
    public class AppBindingProfile : ViewModel
    {
        /// <summary>
        /// Creates a new app-specific override, starting as a copy of the global bindings
        /// (mirrors Wacom: a new app entry starts as a copy of "All other applications").
        /// </summary>
        public static AppBindingProfile CreateFromGlobal(string bundleId, string displayName, BindingSettings global)
        {
            var clonedJson = JsonConvert.SerializeObject(global);
            var cloned = JsonConvert.DeserializeObject<BindingSettings>(clonedJson) ?? new BindingSettings();

            return new AppBindingProfile
            {
                BundleIdentifier = bundleId,
                DisplayName = displayName,
                BindingSettings = cloned
            };
        }

        [JsonProperty(nameof(BundleIdentifier))]
        public required string BundleIdentifier
        {
            get;
            set => this.RaiseAndSetIfChanged(ref field, value);
        }

        [JsonProperty(nameof(DisplayName))]
        public required string DisplayName
        {
            get;
            set => this.RaiseAndSetIfChanged(ref field, value);
        }

        [JsonProperty(nameof(BindingSettings))]
        public BindingSettings BindingSettings
        {
            get;
            set => this.RaiseAndSetIfChanged(ref field, value);
        } = new BindingSettings();
    }
}
