namespace OpenTabletDriver.Desktop.Contracts
{
    /// <summary>A running GUI application, as offered by the app-specific bindings picker.</summary>
    public class RunningApplication
    {
        /// <summary>Match key stored in AppBindingProfile.BundleIdentifier.</summary>
        public string BundleId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
