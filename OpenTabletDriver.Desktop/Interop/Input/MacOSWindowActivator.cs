using System;
using System.Threading;
using OpenTabletDriver.Native.OSX;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    using static ObjectiveCRuntime;
    using static OSX;

    internal sealed class MacOSWindowActivator : IDisposable
    {
        private const int AXSuccess = 0;
        private const ulong NSApplicationActivateAllWindows = 1UL << 0;

        private readonly IntPtr _runningApplicationClass = objc_getClass("NSRunningApplication");
        private readonly IntPtr _runningApplicationWithPidSelector = sel_registerName("runningApplicationWithProcessIdentifier:");
        private readonly IntPtr _activateWithOptionsSelector = sel_registerName("activateWithOptions:");
        private int _activationInProgress;
        private int _disposed;

        public void ActivateAt(CGPoint location)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            if (Interlocked.CompareExchange(ref _activationInProgress, 1, 0) != 0)
                return;

            if (!ThreadPool.QueueUserWorkItem(_ =>
            {
                var pool = objc_autoreleasePoolPush();
                try
                {
                    ActivateAtCore(location);
                }
                finally
                {
                    objc_autoreleasePoolPop(pool);
                    Volatile.Write(ref _activationInProgress, 0);
                }
            }))
            {
                Volatile.Write(ref _activationInProgress, 0);
            }
        }

        private void ActivateAtCore(CGPoint location)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            IntPtr axWindowAttribute = IntPtr.Zero;
            IntPtr axRaiseAction = IntPtr.Zero;
            IntPtr systemWideElement = IntPtr.Zero;
            IntPtr hitElement = IntPtr.Zero;
            IntPtr windowElement = IntPtr.Zero;

            try
            {
                axWindowAttribute = CreateString("AXWindow");
                axRaiseAction = CreateString("AXRaise");
                systemWideElement = AXUIElementCreateSystemWide();
                if (systemWideElement == IntPtr.Zero)
                    return;

                if (AXUIElementCopyElementAtPosition(systemWideElement, (float)location.x, (float)location.y, out hitElement) != AXSuccess || hitElement == IntPtr.Zero)
                    return;

                var targetElement = hitElement;
                if (AXUIElementCopyAttributeValue(hitElement, axWindowAttribute, out windowElement) == AXSuccess && windowElement != IntPtr.Zero)
                    targetElement = windowElement;

                _ = AXUIElementPerformAction(targetElement, axRaiseAction);

                if (TryGetPid(targetElement, out var pid) || targetElement != hitElement && TryGetPid(hitElement, out pid))
                    ActivateApplication(pid);
            }
            catch
            {
            }
            finally
            {
                if (axRaiseAction != IntPtr.Zero)
                    CFRelease(axRaiseAction);
                if (axWindowAttribute != IntPtr.Zero)
                    CFRelease(axWindowAttribute);
                if (windowElement != IntPtr.Zero)
                    CFRelease(windowElement);
                if (hitElement != IntPtr.Zero)
                    CFRelease(hitElement);
                if (systemWideElement != IntPtr.Zero)
                    CFRelease(systemWideElement);
            }
        }

        private static bool TryGetPid(IntPtr element, out int pid)
        {
            if (AXUIElementGetPid(element, out pid) == AXSuccess && pid > 0)
                return true;

            pid = 0;
            return false;
        }

        private void ActivateApplication(int pid)
        {
            if (_runningApplicationClass == IntPtr.Zero)
                return;

            var app = objc_msgSend_IntPtr_int(_runningApplicationClass, _runningApplicationWithPidSelector, pid);
            if (app != IntPtr.Zero)
                _ = objc_msgSend_bool_ulong(app, _activateWithOptionsSelector, NSApplicationActivateAllWindows);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
        }
    }
}
