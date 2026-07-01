using System.Collections.Generic;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    public interface IForegroundAppProvider
    {
        bool TryGetForegroundApp(out string bundleId, out string displayName);

        IEnumerable<(string BundleId, string DisplayName)> GetRunningApplications();

        /// <summary>Resolves an app bundle/executable picked via "Browse..." into a match key + display name.</summary>
        bool TryGetBundleInfo(string path, out string bundleId, out string displayName);
    }
}
