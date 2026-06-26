using System;
using System.Runtime.InteropServices;
using System.Text;
using OpenTabletDriver.Native.OSX;

namespace OpenTabletDriver.Devices.MacOSHid
{
    internal static class IOHID
    {
        private const string IOKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";
        private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const uint CFStringEncodingUTF8 = 0x08000100;
        private const int CFNumberSInt32Type = 3;

        private static readonly IntPtr CoreFoundationHandle = LibSystem.dlopen(CoreFoundationLibrary, 0);
        private static readonly nint CFStringTypeId = CFStringGetTypeID();
        private static readonly nint CFNumberTypeId = CFNumberGetTypeID();
        private static IntPtr cfRunLoopDefaultMode;

        public const int IOHIDOptionsTypeNone = 0;
        public const int IOHIDOptionsTypeSeizeDevice = 1;
        public const int IOHIDReportTypeInput = 0;
        public const int IOHIDReportTypeOutput = 1;
        public const int IOHIDReportTypeFeature = 2;
        public const int IOHIDRequestTypeListenEvent = 1;
        public const int IOHIDAccessTypeGranted = 0;
        public const int IOHIDAccessTypeDenied = 1;
        public const int IOHIDAccessTypeUnknown = 2;

        public static IntPtr CFRunLoopDefaultMode => cfRunLoopDefaultMode == IntPtr.Zero
            ? cfRunLoopDefaultMode = LibSystem.GetConstant(CoreFoundationHandle, "kCFRunLoopDefaultMode")
            : cfRunLoopDefaultMode;

        public static IntPtr CreateString(string value) =>
            CFStringCreateWithCString(IntPtr.Zero, value, CFStringEncodingUTF8);

        public static int GetIntProperty(IntPtr device, string key, int fallback = 0)
        {
            using var cfKey = new CFObject(CreateString(key));
            var property = IOHIDDeviceGetProperty(device, cfKey.Handle);
            if (property == IntPtr.Zero || CFGetTypeID(property) != CFNumberTypeId)
                return fallback;

            return CFNumberGetValue(property, CFNumberSInt32Type, out int value) ? value : fallback;
        }

        public static string? GetStringProperty(IntPtr device, string key)
        {
            using var cfKey = new CFObject(CreateString(key));
            var property = IOHIDDeviceGetProperty(device, cfKey.Handle);
            if (property == IntPtr.Zero || CFGetTypeID(property) != CFStringTypeId)
                return null;

            var buffer = new StringBuilder(512);
            return CFStringGetCString(property, buffer, buffer.Capacity, CFStringEncodingUTF8) ? buffer.ToString() : null;
        }

        public static IntPtr CreateIntDictionary(params (string Key, int Value)[] values)
        {
            var dictionary = CFDictionaryCreateMutable(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            foreach (var (key, value) in values)
            {
                var number = value;
                var cfKey = CreateString(key);
                var cfValue = CFNumberCreate(IntPtr.Zero, CFNumberSInt32Type, ref number);
                CFDictionarySetValue(dictionary, cfKey, cfValue);
            }

            return dictionary;
        }

        public readonly struct CFObject : IDisposable
        {
            public CFObject(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                    CFRelease(Handle);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void IOHIDReportCallback(IntPtr context, int result, IntPtr sender, int type, uint reportId, IntPtr report, nint reportLength);

        [DllImport(IOKitLibrary)]
        public static extern IntPtr IOHIDManagerCreate(IntPtr allocator, int options);

        [DllImport(IOKitLibrary)]
        public static extern void IOHIDManagerSetDeviceMatching(IntPtr manager, IntPtr matching);

        [DllImport(IOKitLibrary)]
        public static extern int IOHIDManagerOpen(IntPtr manager, int options);

        [DllImport(IOKitLibrary)]
        public static extern int IOHIDManagerClose(IntPtr manager, int options);

        [DllImport(IOKitLibrary)]
        public static extern void IOHIDManagerRegisterInputReportCallback(IntPtr manager, IOHIDReportCallback callback, IntPtr context);

        [DllImport(IOKitLibrary)]
        public static extern void IOHIDManagerScheduleWithRunLoop(IntPtr manager, IntPtr runLoop, IntPtr runLoopMode);

        [DllImport(IOKitLibrary)]
        public static extern void IOHIDManagerUnscheduleFromRunLoop(IntPtr manager, IntPtr runLoop, IntPtr runLoopMode);

        [DllImport(IOKitLibrary)]
        public static extern int IOHIDCheckAccess(int requestType);

        [DllImport(IOKitLibrary)]
        public static extern bool IOHIDRequestAccess(int requestType);

        [DllImport(IOKitLibrary)]
        public static extern IntPtr IOHIDManagerCopyDevices(IntPtr manager);

        [DllImport(IOKitLibrary)]
        public static extern IntPtr IOHIDDeviceGetProperty(IntPtr device, IntPtr key);

        [DllImport(IOKitLibrary)]
        public static extern int IOHIDDeviceOpen(IntPtr device, int options);

        [DllImport(IOKitLibrary)]
        public static extern void IOHIDDeviceClose(IntPtr device, int options);

        [DllImport(IOKitLibrary)]
        public static extern int IOHIDDeviceGetReport(IntPtr device, int reportType, uint reportId, IntPtr report, ref nint reportLength);

        [DllImport(IOKitLibrary)]
        public static extern int IOHIDDeviceSetReport(IntPtr device, int reportType, uint reportId, byte[] report, nint reportLength);

        [DllImport(IOKitLibrary)]
        public static extern void IOHIDDeviceRegisterInputReportCallback(IntPtr device, IntPtr report, nint reportLength, IOHIDReportCallback callback, IntPtr context);

        [DllImport(IOKitLibrary)]
        public static extern void IOHIDDeviceScheduleWithRunLoop(IntPtr device, IntPtr runLoop, IntPtr runLoopMode);

        [DllImport(IOKitLibrary)]
        public static extern void IOHIDDeviceUnscheduleFromRunLoop(IntPtr device, IntPtr runLoop, IntPtr runLoopMode);

        [DllImport(CoreFoundationLibrary)]
        public static extern nint CFSetGetCount(IntPtr set);

        [DllImport(CoreFoundationLibrary)]
        public static extern void CFSetGetValues(IntPtr set, [Out] IntPtr[] values);

        [DllImport(CoreFoundationLibrary)]
        public static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

        [DllImport(CoreFoundationLibrary)]
        public static extern bool CFStringGetCString(IntPtr value, StringBuilder buffer, long bufferSize, uint encoding);

        [DllImport(CoreFoundationLibrary)]
        public static extern bool CFNumberGetValue(IntPtr number, int type, out int value);

        [DllImport(CoreFoundationLibrary)]
        public static extern IntPtr CFNumberCreate(IntPtr allocator, int type, ref int value);

        [DllImport(CoreFoundationLibrary)]
        public static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

        [DllImport(CoreFoundationLibrary)]
        public static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

        [DllImport(CoreFoundationLibrary)]
        public static extern nint CFGetTypeID(IntPtr value);

        [DllImport(CoreFoundationLibrary)]
        public static extern nint CFStringGetTypeID();

        [DllImport(CoreFoundationLibrary)]
        public static extern nint CFNumberGetTypeID();

        [DllImport(CoreFoundationLibrary)]
        public static extern void CFRelease(IntPtr value);

        [DllImport(CoreFoundationLibrary)]
        public static extern IntPtr CFRetain(IntPtr value);

        [DllImport(CoreFoundationLibrary)]
        public static extern IntPtr CFRunLoopGetCurrent();

        [DllImport(CoreFoundationLibrary)]
        public static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, bool returnAfterSourceHandled);

        [DllImport(CoreFoundationLibrary)]
        public static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);

        [DllImport(CoreFoundationLibrary)]
        public static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

        [DllImport(CoreFoundationLibrary)]
        public static extern void CFRunLoopRemoveSource(IntPtr runLoop, IntPtr source, IntPtr mode);

        [DllImport(CoreFoundationLibrary)]
        public static extern void CFRunLoopStop(IntPtr runLoop);

        [DllImport(CoreFoundationLibrary)]
        public static extern void CFRunLoopWakeUp(IntPtr runLoop);
    }
}
