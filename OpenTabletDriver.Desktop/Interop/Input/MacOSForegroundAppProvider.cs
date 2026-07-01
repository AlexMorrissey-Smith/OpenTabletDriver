using System;
using System.Collections.Generic;
using OpenTabletDriver.Native.OSX;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    using static CoreFoundation;
    using static ObjectiveCRuntime;
    using static OSX;

    internal sealed class MacOSForegroundAppProvider : IForegroundAppProvider
    {
        private const int NSApplicationActivationPolicyRegular = 0;
        private const uint WindowListOptions = CGWindowListOptionOnScreenOnly | CGWindowListExcludeDesktopElements;

        private readonly IntPtr _workspaceClass = objc_getClass("NSWorkspace");
        private readonly IntPtr _sharedWorkspaceSelector = sel_registerName("sharedWorkspace");
        private readonly IntPtr _runningApplicationsSelector = sel_registerName("runningApplications");
        private readonly IntPtr _bundleIdentifierSelector = sel_registerName("bundleIdentifier");
        private readonly IntPtr _localizedNameSelector = sel_registerName("localizedName");
        private readonly IntPtr _activationPolicySelector = sel_registerName("activationPolicy");

        private readonly IntPtr _runningApplicationClass = objc_getClass("NSRunningApplication");
        private readonly IntPtr _runningApplicationWithPidSelector = sel_registerName("runningApplicationWithProcessIdentifier:");

        private readonly IntPtr _bundleClass = objc_getClass("NSBundle");
        private readonly IntPtr _bundleWithPathSelector = sel_registerName("bundleWithPath:");

        // NSWorkspace.frontmostApplication returns a STALE value from a background daemon
        // (it latches to whatever was frontmost when the process started). The frontmost
        // on-screen window's owner, read live via CGWindowList (the same API
        // MacOSWindowActivator uses successfully from this daemon), reflects the real
        // focused app at button-press time.
        public bool TryGetForegroundApp(out string bundleId, out string displayName)
        {
            bundleId = string.Empty;
            displayName = string.Empty;

            var windowList = CGWindowListCopyWindowInfo(WindowListOptions, CGNullWindowID);
            if (windowList == IntPtr.Zero)
                return false;

            var ownerPidKey = CreateString("kCGWindowOwnerPID");
            var layerKey = CreateString("kCGWindowLayer");
            try
            {
                long count = CFArrayGetCount(windowList);
                for (long i = 0; i < count; i++)
                {
                    var window = CFArrayGetValueAtIndex(windowList, i);
                    if (window == IntPtr.Zero)
                        continue;

                    // Windows are front-to-back; the first normal-layer (0) window belongs
                    // to the focused app. Higher layers are menus/overlays/system UI.
                    if (!TryGetInt(window, layerKey, out var layer) || layer != 0)
                        continue;

                    if (!TryGetInt(window, ownerPidKey, out var pid) || pid <= 0)
                        continue;

                    return TryReadAppByPid(pid, out bundleId, out displayName);
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                CoreFoundation.CFRelease(layerKey);
                CoreFoundation.CFRelease(ownerPidKey);
                CoreFoundation.CFRelease(windowList);
            }
        }

        private bool TryReadAppByPid(int pid, out string bundleId, out string displayName)
        {
            bundleId = string.Empty;
            displayName = string.Empty;

            if (_runningApplicationClass == IntPtr.Zero)
                return false;

            var pool = objc_autoreleasePoolPush();
            try
            {
                var app = objc_msgSend_IntPtr_int(_runningApplicationClass, _runningApplicationWithPidSelector, pid);
                if (app == IntPtr.Zero)
                    return false;

                return TryReadApp(app, out bundleId, out displayName);
            }
            catch
            {
                return false;
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }

        private static bool TryGetInt(IntPtr dictionary, IntPtr key, out int value)
        {
            value = 0;
            var number = CFDictionaryGetValue(dictionary, key);
            return number != IntPtr.Zero && CFNumberGetValue(number, kCFNumberIntType, out value);
        }

        public IEnumerable<(string BundleId, string DisplayName)> GetRunningApplications()
        {
            var results = new List<(string, string)>();

            if (_workspaceClass == IntPtr.Zero)
                return results;

            var pool = objc_autoreleasePoolPush();
            try
            {
                var workspace = objc_msgSend_IntPtr(_workspaceClass, _sharedWorkspaceSelector);
                if (workspace == IntPtr.Zero)
                    return results;

                var apps = objc_msgSend_IntPtr(workspace, _runningApplicationsSelector);
                if (apps == IntPtr.Zero)
                    return results;

                long count = CFArrayGetCount(apps);
                for (long i = 0; i < count; i++)
                {
                    var app = CFArrayGetValueAtIndex(apps, i);
                    if (app == IntPtr.Zero)
                        continue;

                    var policy = (int)objc_msgSend_IntPtr(app, _activationPolicySelector);
                    if (policy != NSApplicationActivationPolicyRegular)
                        continue;

                    if (TryReadApp(app, out var bundleId, out var displayName))
                        results.Add((bundleId, displayName));
                }
            }
            catch
            {
                // best-effort; return whatever was gathered before the failure
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }

            return results;
        }

        public bool TryGetBundleInfo(string path, out string bundleId, out string displayName)
        {
            bundleId = string.Empty;
            displayName = System.IO.Path.GetFileNameWithoutExtension(path);

            if (_bundleClass == IntPtr.Zero)
                return false;

            var pool = objc_autoreleasePoolPush();
            var pathString = CreateString(path);
            try
            {
                var bundle = objc_msgSend_IntPtr_IntPtr(_bundleClass, _bundleWithPathSelector, pathString);
                if (bundle == IntPtr.Zero)
                    return false;

                var bundleIdString = objc_msgSend_IntPtr(bundle, _bundleIdentifierSelector);
                bundleId = GetString(bundleIdString) ?? string.Empty;
                return !string.IsNullOrEmpty(bundleId);
            }
            catch
            {
                return false;
            }
            finally
            {
                CoreFoundation.CFRelease(pathString);
                objc_autoreleasePoolPop(pool);
            }
        }

        private bool TryReadApp(IntPtr application, out string bundleId, out string displayName)
        {
            var bundleIdString = objc_msgSend_IntPtr(application, _bundleIdentifierSelector);
            bundleId = GetString(bundleIdString) ?? string.Empty;

            var nameString = objc_msgSend_IntPtr(application, _localizedNameSelector);
            displayName = GetString(nameString) ?? bundleId;

            return !string.IsNullOrEmpty(bundleId);
        }
    }
}
