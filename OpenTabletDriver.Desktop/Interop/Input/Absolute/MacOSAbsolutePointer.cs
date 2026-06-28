using System;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Native.OSX.Input;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace OpenTabletDriver.Desktop.Interop.Input.Absolute
{
    using static OSX;

    public class MacOSAbsolutePointer : MacOSVirtualMouse, IAbsolutePointer
    {
        private const float DockEdgeSnapDistance = 2f;

        private Vector2 _offset;
        private Vector2? _lastPos;
        private Vector2? _delta;
        private readonly Vector2 _min;
        private readonly Vector2 _max;

        public MacOSAbsolutePointer()
        {
            var virtualScreen = DesktopInterop.VirtualScreen
                                ?? throw new InvalidOperationException("Could not get virtual screen");
            var primary = virtualScreen.Displays.First();
            _offset = primary.Position;
            _min = primary.Position - _offset;
            _max = primary.Position + new Vector2(primary.Width - 1, primary.Height - 1) - _offset;
        }

        public void SetPosition(Vector2 pos)
        {
            var newPos = pos - _offset;
            newPos = SnapToScreenEdge(newPos);
            _delta = newPos - _lastPos;
            _lastPos = newPos;

            QueuePendingPosition(newPos.X, newPos.Y);
        }

        private Vector2 SnapToScreenEdge(Vector2 pos)
        {
            if (pos.X <= _min.X + DockEdgeSnapDistance)
                pos.X = _min.X;
            else if (pos.X >= _max.X - DockEdgeSnapDistance)
                pos.X = _max.X;

            if (pos.Y <= _min.Y + DockEdgeSnapDistance)
                pos.Y = _min.Y;
            else if (pos.Y >= _max.Y - DockEdgeSnapDistance)
                pos.Y = _max.Y;

            return pos;
        }

        protected override void SetPendingPosition(IntPtr mouseEvent, float x, float y)
        {
            CGEventSetLocation(mouseEvent, new CGPoint(x, y));
            if (_delta is not null)
            {
                CGEventSetDoubleValueField(mouseEvent, CGEventField.mouseEventDeltaX, _delta.Value.X);
                CGEventSetDoubleValueField(mouseEvent, CGEventField.mouseEventDeltaY, _delta.Value.Y);
            }
        }

        protected override void QueuePendingPositionFromSystem()
        {
            var eventRef = CGEventCreate(IntPtr.Zero);
            var pos = CGEventGetLocation(eventRef);
            CFRelease(eventRef);
            QueuePendingPosition((float)pos.x, (float)pos.y);
        }

        protected override void ResetPendingPosition(IntPtr mouseEvent)
        {
            _lastPos = null;
            _delta = null;
        }
    }
}
