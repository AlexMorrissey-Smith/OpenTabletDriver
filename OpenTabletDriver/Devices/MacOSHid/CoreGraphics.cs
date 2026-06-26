using System;
using System.Runtime.InteropServices;

namespace OpenTabletDriver.Devices.MacOSHid
{
    internal static class CoreGraphics
    {
        private const string CoreGraphicsLibrary = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

        public const uint kCGHIDEventTap = 0;
        public const uint kCGSessionEventTap = 1;
        public const uint kCGHeadInsertEventTap = 0;
        public const uint kCGEventTapOptionDefault = 0;

        public const uint kCGEventLeftMouseDown = 1;
        public const uint kCGEventLeftMouseUp = 2;
        public const uint kCGEventRightMouseDown = 3;
        public const uint kCGEventRightMouseUp = 4;
        public const uint kCGEventMouseMoved = 5;
        public const uint kCGEventLeftMouseDragged = 6;
        public const uint kCGEventRightMouseDragged = 7;
        public const uint kCGEventScrollWheel = 22;
        public const uint kCGEventTabletPointer = 23;
        public const uint kCGEventTabletProximity = 24;
        public const uint kCGEventOtherMouseDown = 25;
        public const uint kCGEventOtherMouseUp = 26;
        public const uint kCGEventOtherMouseDragged = 27;
        public const uint kCGEventTapDisabledByTimeout = 0xFFFFFFFE;
        public const uint kCGEventTapDisabledByUserInput = 0xFFFFFFFF;

        public const int kCGMouseEventDeltaX = 4;
        public const int kCGMouseEventDeltaY = 5;
        public const int kCGMouseEventSubtype = 7;
        public const int kCGEventMouseSubtypeTabletPoint = 1;
        public const int kCGEventMouseSubtypeTabletProximity = 2;

        public const int kCGTabletEventPointX = 15;
        public const int kCGTabletEventPointY = 16;
        public const int kCGTabletEventPointButtons = 18;
        public const int kCGTabletEventPointPressure = 19;
        public const int kCGTabletEventTiltX = 20;
        public const int kCGTabletEventTiltY = 21;
        public const int kCGTabletProximityEventEnterProximity = 38;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr CGEventTapCallBack(IntPtr proxy, uint type, IntPtr eventRef, IntPtr userInfo);

        [StructLayout(LayoutKind.Sequential)]
        public struct CGPoint
        {
            public double X;
            public double Y;
        }

        public static ulong Mask(params uint[] eventTypes)
        {
            ulong mask = 0;
            foreach (var eventType in eventTypes)
                mask |= 1UL << (int)eventType;

            return mask;
        }

        [DllImport(CoreGraphicsLibrary)]
        public static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong eventsOfInterest, CGEventTapCallBack callback, IntPtr userInfo);

        [DllImport(CoreGraphicsLibrary)]
        public static extern void CGEventTapEnable(IntPtr tap, bool enable);

        [DllImport(CoreGraphicsLibrary)]
        public static extern long CGEventGetIntegerValueField(IntPtr eventRef, int field);

        [DllImport(CoreGraphicsLibrary)]
        public static extern double CGEventGetDoubleValueField(IntPtr eventRef, int field);

        [DllImport(CoreGraphicsLibrary)]
        public static extern CGPoint CGEventGetLocation(IntPtr eventRef);
    }
}
