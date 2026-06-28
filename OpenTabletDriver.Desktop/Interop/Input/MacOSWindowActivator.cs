using System;
using System.Diagnostics;
using System.Threading;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Native.OSX.Generic;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    using static CoreFoundation;
    using static ObjectiveCRuntime;
    using static OSX;

    internal sealed class MacOSWindowActivator : IDisposable
    {
        private const int AXSuccess = 0;
        private const uint WindowListOptions = CGWindowListOptionOnScreenOnly | CGWindowListExcludeDesktopElements;
        private const int TargetCacheMaxAgeInMs = 2000;
        private const int TargetUpdateThrottleInMs = 50;
        private const double TargetCacheTolerance = 96;
        private const ulong NSApplicationActivateAllWindows = 1UL << 0;
        private const ulong NSApplicationActivateIgnoringOtherApps = 1UL << 1;

        private readonly IntPtr _workspaceClass = objc_getClass("NSWorkspace");
        private readonly IntPtr _sharedWorkspaceSelector = sel_registerName("sharedWorkspace");
        private readonly IntPtr _frontmostApplicationSelector = sel_registerName("frontmostApplication");
        private readonly IntPtr _processIdentifierSelector = sel_registerName("processIdentifier");
        private readonly IntPtr _runningApplicationClass = objc_getClass("NSRunningApplication");
        private readonly IntPtr _runningApplicationWithPidSelector = sel_registerName("runningApplicationWithProcessIdentifier:");
        private readonly IntPtr _activateWithOptionsSelector = sel_registerName("activateWithOptions:");
        private readonly IntPtr _axRaiseAction = CreateString("AXRaise");
        private readonly object _cacheLock = new();
        private CGPoint _cachedLocation;
        private IntPtr _cachedWindowElement;
        private int _cachedPid;
        private MacOSPointerTargetKind _cachedTargetKind;
        private long _cachedTimestamp;
        private long _lastUpdateQueuedTimestamp;
        private int _updateInProgress;
        private int _activationInProgress;
        private int _loggedActivationResult;
        private int _loggedSystemUiSkip;
        private int _disposed;

        public void QueueTargetUpdate(CGPoint location)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            var now = Stopwatch.GetTimestamp();
            var lastUpdateQueuedTimestamp = Volatile.Read(ref _lastUpdateQueuedTimestamp);
            if (lastUpdateQueuedTimestamp != 0 && ElapsedMilliseconds(lastUpdateQueuedTimestamp, now) < TargetUpdateThrottleInMs)
                return;

            if (Interlocked.CompareExchange(ref _updateInProgress, 1, 0) != 0)
                return;

            Volatile.Write(ref _lastUpdateQueuedTimestamp, now);
            if (!ThreadPool.QueueUserWorkItem(_ => UpdateTargetCore(location)))
                Volatile.Write(ref _updateInProgress, 0);
        }

        public MacOSPointerTargetKind GetCachedTargetKind(CGPoint location)
        {
            lock (_cacheLock)
            {
                if (_cachedPid <= 0)
                    return MacOSPointerTargetKind.Unknown;

                if (ElapsedMilliseconds(_cachedTimestamp, Stopwatch.GetTimestamp()) > TargetCacheMaxAgeInMs)
                    return MacOSPointerTargetKind.Unknown;

                var dx = _cachedLocation.x - location.x;
                var dy = _cachedLocation.y - location.y;
                if (dx * dx + dy * dy > TargetCacheTolerance * TargetCacheTolerance)
                    return MacOSPointerTargetKind.Unknown;

                return _cachedTargetKind;
            }
        }

        public void ActivateAt(CGPoint location)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            var pool = objc_autoreleasePoolPush();
            var started = Stopwatch.GetTimestamp();
            try
            {
                MacOSPointerTargetKind targetKind;
                if (!TryGetWindowOwnerAt(location, out var pid))
                {
                    if (!TryGetCachedTarget(location, out pid, out var cachedWindowElement, out targetKind))
                        return;

                    if (cachedWindowElement != IntPtr.Zero)
                        CoreFoundation.CFRelease(cachedWindowElement);
                }
                else
                    targetKind = ClassifyTarget(pid);

                if (targetKind == MacOSPointerTargetKind.SystemUi)
                {
                    _ = IsSystemUiTarget(pid, out var processName);
                    if (Interlocked.Exchange(ref _loggedSystemUiSkip, 1) == 0)
                        Log.Debug("macOS Window Activator", $"Skipping explicit app activation for system UI target pid={pid} process={processName}.");
                    return;
                }

                ActivateApplication(pid);
                if (Interlocked.Exchange(ref _loggedActivationResult, 1) == 0)
                    Log.Debug("macOS Window Activator", $"Activated target pid={pid} elapsed={ElapsedMilliseconds(started, Stopwatch.GetTimestamp()):0.##}ms.");
            }
            catch
            {
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }

        public void ActivateCachedAt(CGPoint location)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            if (!TryGetCachedTarget(location, out var pid, out var windowElement, out var targetKind))
                return;

            var pool = objc_autoreleasePoolPush();
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (targetKind == MacOSPointerTargetKind.SystemUi)
                {
                    _ = IsSystemUiTarget(pid, out var processName);
                    if (Interlocked.Exchange(ref _loggedSystemUiSkip, 1) == 0)
                        Log.Debug("macOS Window Activator", $"Skipping explicit app activation for system UI target pid={pid} process={processName}.");
                    return;
                }

                if (targetKind != MacOSPointerTargetKind.BackgroundApplication)
                    return;

                ActivateApplication(pid);
                MarkCachedTargetKind(pid, MacOSPointerTargetKind.ActiveApplication);
                if (Interlocked.Exchange(ref _loggedActivationResult, 1) == 0)
                    Log.Debug("macOS Window Activator", $"Activated cached target pid={pid} elapsed={ElapsedMilliseconds(started, Stopwatch.GetTimestamp()):0.##}ms.");
            }
            catch
            {
            }
            finally
            {
                if (windowElement != IntPtr.Zero)
                    CoreFoundation.CFRelease(windowElement);
                objc_autoreleasePoolPop(pool);
            }
        }

        private void QueueActivateTarget(int pid, IntPtr windowElement)
        {
            if (Interlocked.CompareExchange(ref _activationInProgress, 1, 0) != 0)
            {
                if (windowElement != IntPtr.Zero)
                    CoreFoundation.CFRelease(windowElement);
                return;
            }

            if (!ThreadPool.QueueUserWorkItem(_ =>
            {
                var pool = objc_autoreleasePoolPush();
                try
                {
                    ActivateTarget(pid, windowElement);
                }
                catch
                {
                }
                finally
                {
                    if (windowElement != IntPtr.Zero)
                        CoreFoundation.CFRelease(windowElement);
                    objc_autoreleasePoolPop(pool);
                    Volatile.Write(ref _activationInProgress, 0);
                }
            }))
            {
                if (windowElement != IntPtr.Zero)
                    CoreFoundation.CFRelease(windowElement);
                Volatile.Write(ref _activationInProgress, 0);
            }
        }

        private void UpdateTargetCore(CGPoint location)
        {
            var pool = objc_autoreleasePoolPush();
            var pid = 0;
            var windowElement = IntPtr.Zero;
            try
            {
                _ = TryGetTargetAt(location, out pid, out windowElement);

                if (Volatile.Read(ref _disposed) != 0)
                    return;

                CacheTarget(location, pid, windowElement, retainWindowElement: false);
                windowElement = IntPtr.Zero;
            }
            catch
            {
            }
            finally
            {
                if (windowElement != IntPtr.Zero)
                    CoreFoundation.CFRelease(windowElement);
                objc_autoreleasePoolPop(pool);
                Volatile.Write(ref _updateInProgress, 0);
            }
        }

        private void CacheTarget(CGPoint location, int pid, IntPtr windowElement, bool retainWindowElement)
        {
            var targetKind = ClassifyTarget(pid);

            lock (_cacheLock)
            {
                if (_cachedWindowElement != IntPtr.Zero)
                    CoreFoundation.CFRelease(_cachedWindowElement);

                _cachedLocation = location;
                _cachedWindowElement = retainWindowElement && windowElement != IntPtr.Zero
                    ? CoreFoundation.CFRetain(windowElement)
                    : windowElement;
                _cachedPid = pid;
                _cachedTargetKind = targetKind;
                _cachedTimestamp = Stopwatch.GetTimestamp();
            }
        }

        private void MarkCachedTargetKind(int pid, MacOSPointerTargetKind targetKind)
        {
            lock (_cacheLock)
            {
                if (_cachedPid == pid)
                    _cachedTargetKind = targetKind;
            }
        }

        private bool TryGetCachedTarget(CGPoint location, out int pid, out IntPtr windowElement, out MacOSPointerTargetKind targetKind)
        {
            pid = 0;
            windowElement = IntPtr.Zero;
            targetKind = MacOSPointerTargetKind.Unknown;

            lock (_cacheLock)
            {
                if (_cachedPid <= 0)
                    return false;

                if (ElapsedMilliseconds(_cachedTimestamp, Stopwatch.GetTimestamp()) > TargetCacheMaxAgeInMs)
                    return false;

                var dx = _cachedLocation.x - location.x;
                var dy = _cachedLocation.y - location.y;
                if (dx * dx + dy * dy > TargetCacheTolerance * TargetCacheTolerance)
                    return false;

                pid = _cachedPid;
                targetKind = _cachedTargetKind;
                if (_cachedWindowElement != IntPtr.Zero)
                    windowElement = CoreFoundation.CFRetain(_cachedWindowElement);
                return true;
            }
        }

        private static bool TryGetAccessibilityTargetAt(CGPoint location, out int pid, out IntPtr windowElement)
        {
            pid = 0;
            windowElement = IntPtr.Zero;

            var axWindowAttribute = CreateString("AXWindow");
            var systemWideElement = IntPtr.Zero;
            var hitElement = IntPtr.Zero;
            var targetElement = IntPtr.Zero;

            try
            {
                systemWideElement = AXUIElementCreateSystemWide();
                if (systemWideElement == IntPtr.Zero)
                    return false;

                if (AXUIElementCopyElementAtPosition(systemWideElement, (float)location.x, (float)location.y, out hitElement) != AXSuccess || hitElement == IntPtr.Zero)
                    return false;

                if (AXUIElementCopyAttributeValue(hitElement, axWindowAttribute, out targetElement) != AXSuccess || targetElement == IntPtr.Zero)
                    targetElement = CoreFoundation.CFRetain(hitElement);

                if (!TryGetPid(targetElement, out pid) && !TryGetPid(hitElement, out pid))
                    return false;

                windowElement = targetElement;
                targetElement = IntPtr.Zero;
                return pid > 0;
            }
            finally
            {
                if (targetElement != IntPtr.Zero)
                    CoreFoundation.CFRelease(targetElement);
                if (hitElement != IntPtr.Zero)
                    CoreFoundation.CFRelease(hitElement);
                if (systemWideElement != IntPtr.Zero)
                    CoreFoundation.CFRelease(systemWideElement);
                CoreFoundation.CFRelease(axWindowAttribute);
            }
        }

        private static bool TryGetPid(IntPtr element, out int pid)
        {
            if (element != IntPtr.Zero && AXUIElementGetPid(element, out pid) == AXSuccess && pid > 0)
                return true;

            pid = 0;
            return false;
        }

        private static bool TryGetTargetAt(CGPoint location, out int pid, out IntPtr windowElement)
        {
            windowElement = IntPtr.Zero;
            if (TryGetWindowOwnerAt(location, out pid))
                return true;

            return TryGetAccessibilityTargetAt(location, out pid, out windowElement);
        }

        private static bool TryGetWindowOwnerAt(CGPoint location, out int pid)
        {
            pid = 0;

            var windowList = CGWindowListCopyWindowInfo(WindowListOptions, CGNullWindowID);
            if (windowList == IntPtr.Zero)
                return false;

            var ownerPidKey = CreateString("kCGWindowOwnerPID");
            var layerKey = CreateString("kCGWindowLayer");
            var boundsKey = CreateString("kCGWindowBounds");

            try
            {
                var count = CFArrayGetCount(windowList);
                for (long i = 0; i < count; i++)
                {
                    var window = CFArrayGetValueAtIndex(windowList, i);
                    if (window == IntPtr.Zero)
                        continue;

                    if (!TryGetInt(window, layerKey, out var layer) || layer != 0)
                        continue;

                    if (!TryGetBounds(window, boundsKey, out var bounds) || !Contains(bounds, location))
                        continue;

                    return TryGetInt(window, ownerPidKey, out pid) && pid > 0;
                }

                return false;
            }
            finally
            {
                CoreFoundation.CFRelease(boundsKey);
                CoreFoundation.CFRelease(layerKey);
                CoreFoundation.CFRelease(ownerPidKey);
                CoreFoundation.CFRelease(windowList);
            }
        }

        private static bool TryGetBounds(IntPtr window, IntPtr boundsKey, out CGRect bounds)
        {
            bounds = default;

            var boundsDictionary = CFDictionaryGetValue(window, boundsKey);
            if (boundsDictionary == IntPtr.Zero)
                return false;

            var xKey = CreateString("X");
            var yKey = CreateString("Y");
            var widthKey = CreateString("Width");
            var heightKey = CreateString("Height");

            try
            {
                if (!TryGetDouble(boundsDictionary, xKey, out var x) ||
                    !TryGetDouble(boundsDictionary, yKey, out var y) ||
                    !TryGetDouble(boundsDictionary, widthKey, out var width) ||
                    !TryGetDouble(boundsDictionary, heightKey, out var height))
                {
                    return false;
                }

                bounds = new CGRect(new CGPoint(x, y), new CGSize(width, height));
                return true;
            }
            finally
            {
                CoreFoundation.CFRelease(heightKey);
                CoreFoundation.CFRelease(widthKey);
                CoreFoundation.CFRelease(yKey);
                CoreFoundation.CFRelease(xKey);
            }
        }

        private static bool TryGetInt(IntPtr dictionary, IntPtr key, out int value)
        {
            value = 0;

            var number = CFDictionaryGetValue(dictionary, key);
            return number != IntPtr.Zero && CFNumberGetValue(number, kCFNumberIntType, out value);
        }

        private static bool TryGetDouble(IntPtr dictionary, IntPtr key, out double value)
        {
            value = 0;

            var number = CFDictionaryGetValue(dictionary, key);
            return number != IntPtr.Zero && CFNumberGetValue(number, kCFNumberDoubleType, out value);
        }

        private static bool Contains(CGRect bounds, CGPoint location) =>
            location.x >= bounds.origin.x &&
            location.x < bounds.origin.x + bounds.size.width &&
            location.y >= bounds.origin.y &&
            location.y < bounds.origin.y + bounds.size.height;

        private void ActivateTarget(int pid, IntPtr windowElement)
        {
            if (windowElement != IntPtr.Zero)
                _ = AXUIElementPerformAction(windowElement, _axRaiseAction);

            ActivateApplication(pid);
        }

        private void ActivateApplication(int pid)
        {
            if (_runningApplicationClass == IntPtr.Zero)
                return;

            var app = objc_msgSend_IntPtr_int(_runningApplicationClass, _runningApplicationWithPidSelector, pid);
            if (app != IntPtr.Zero)
                _ = objc_msgSend_bool_ulong(app, _activateWithOptionsSelector, NSApplicationActivateAllWindows | NSApplicationActivateIgnoringOtherApps);
        }

        private MacOSPointerTargetKind ClassifyTarget(int pid)
        {
            if (pid <= 0)
                return MacOSPointerTargetKind.Unknown;

            if (IsSystemUiTarget(pid, out _))
                return MacOSPointerTargetKind.SystemUi;

            if (!TryGetFrontmostApplicationPid(out var activePid))
                return MacOSPointerTargetKind.Unknown;

            return activePid == pid
                ? MacOSPointerTargetKind.ActiveApplication
                : MacOSPointerTargetKind.BackgroundApplication;
        }

        private bool TryGetFrontmostApplicationPid(out int pid)
        {
            pid = 0;
            if (_workspaceClass == IntPtr.Zero)
                return false;

            var pool = objc_autoreleasePoolPush();
            try
            {
                var workspace = objc_msgSend_IntPtr(_workspaceClass, _sharedWorkspaceSelector);
                if (workspace == IntPtr.Zero)
                    return false;

                var application = objc_msgSend_IntPtr(workspace, _frontmostApplicationSelector);
                if (application == IntPtr.Zero)
                    return false;

                pid = objc_msgSend_int(application, _processIdentifierSelector);
                return pid > 0;
            }
            catch
            {
                pid = 0;
                return false;
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }

        private static bool IsSystemUiTarget(int pid, out string processName)
        {
            processName = string.Empty;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
            }
            catch
            {
                return false;
            }

            return processName is "Dock" or "SystemUIServer" or "ControlCenter" or "NotificationCenter";
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            lock (_cacheLock)
            {
                if (_cachedWindowElement != IntPtr.Zero)
                {
                    CoreFoundation.CFRelease(_cachedWindowElement);
                    _cachedWindowElement = IntPtr.Zero;
                }
            }

            CoreFoundation.CFRelease(_axRaiseAction);
        }

        private static double ElapsedMilliseconds(long startTimestamp, long endTimestamp) =>
            (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }
}
