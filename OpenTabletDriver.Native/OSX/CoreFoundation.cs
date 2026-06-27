using System;
using System.Runtime.InteropServices;

namespace OpenTabletDriver.Native.OSX
{
    static public class CoreFoundation
    {
        private const string CFLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const uint CFStringEncodingUTF8 = 0x08000100;
        private static IntPtr handle = LibSystem.dlopen(CFLib, 0);

        public const int kCFNumberIntType = 9;
        public const int kCFNumberDoubleType = 13;

        public static IntPtr kCFBooleanTrue = LibSystem.GetConstant(handle, "kCFBooleanTrue");
        public static IntPtr kCFBooleanFalse = LibSystem.GetConstant(handle, "kCFBooleanFalse");

        [DllImport(CFLib)]
        public static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, long capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

        [DllImport(CFLib)]
        public static extern void CFDictionaryAddValue(IntPtr theDict, IntPtr key, IntPtr value);

        [DllImport(CFLib)]
        public static extern void CFRelease(IntPtr cf);

        [DllImport(CFLib)]
        public static extern IntPtr CFRetain(IntPtr cf);

        [DllImport(CFLib)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

        public static IntPtr CreateString(string value) =>
            CFStringCreateWithCString(IntPtr.Zero, value, CFStringEncodingUTF8);

        [DllImport(CFLib)]
        public static extern long CFArrayGetCount(IntPtr array);

        [DllImport(CFLib)]
        public static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);

        [DllImport(CFLib)]
        public static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

        [DllImport(CFLib)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool CFNumberGetValue(IntPtr number, int type, out int value);

        [DllImport(CFLib)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool CFNumberGetValue(IntPtr number, int type, out double value);
    }
}
