using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using OpenTabletDriver.Desktop.Interop.Input.Keyboard;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Native.OSX.Input;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    using static OSX;

    public abstract class MacOSVirtualMouse : IMouseButtonHandler, IMouseScrollHandler, ISynchronousPointer, ITiltHandler, IEraserHandler, IPressureHandler
    {
        private const int DoubleClickMoveTolerance = 8;

        // The capabilityMask is essential for Adobe software to recognize tablet pressure sensitivity,
        // a feature specific to Wacom devices.
        private const WacomCapabilityMask TabletCapabilityMask = WacomCapabilityMask.absXBitMask |
                                                                  WacomCapabilityMask.absYBitMask |
                                                                  WacomCapabilityMask.buttonsBitMask |
                                                                  WacomCapabilityMask.pressureBitMask |
                                                                  WacomCapabilityMask.tiltXBitMask |
                                                                  WacomCapabilityMask.tiltYBitMask |
                                                                  WacomCapabilityMask.deviceIdBitMask;

        // https://www.wacomeng.com/mac/Developers%20Guide.htm#vendorPointerType
        private const long VendorPointerType = 0x802; // General Stylus

        // A non-zero deviceId is essential for Adobe software to map tablet events to their
        // corresponding capabilities. Randomly generated and hopefully do not conflict with another driver.
        private const long DeviceId = 5303613955435230461;

        private int _currButtonStates;
        private int _prevButtonStates;
        private float? _pendingX;
        private float? _pendingY;
        private int? _scrollDeltaX;
        private int? _scrollDeltaY;
        private float? _pressure;
        private Vector2? _tilt;
        private bool? _isEraser;
        private CGMouseButton _lastButton;
        private Vector2 _lastMouseDownPosition;
        private bool _mouseMovedSinceLastDown;
        private MacOSPointerTargetKind _mouseEventTargetKind = MacOSPointerTargetKind.Unknown;
        private MacOSPointerTargetKind _forcedPlainMouseTargetKind = MacOSPointerTargetKind.Unknown;
        private bool _forcePlainMouseUntilRelease;
        private CGMouseButton _mouseEventButton;
        private static int _timingLogs;

        private int _clickState;
        private readonly Stopwatch _stopWatch;
        private readonly Stopwatch _doubleClickStopWatch;
        private readonly IntPtr _mouseEventSource;
        private readonly IntPtr _tabletEventSource;
        private IntPtr _mouseEvent;
        private CGEventType _mouseEventType;
        private readonly double _doubleClickIntervalInMs;
        private readonly MacOSVirtualKeyboard _keyboard;
        private readonly MacOSWindowActivator _windowActivator;

        public MacOSVirtualMouse()
        {
            _doubleClickIntervalInMs = GetDoubleClickInterval() * 1000;
            _doubleClickStopWatch = new Stopwatch();
            _stopWatch = new Stopwatch();
            _stopWatch.Start();
            _mouseEventSource = CGEventSourceCreate(CGEventSourceStateHIDSystemState);
            _tabletEventSource = CGEventSourceCreate(CGEventSourceStatePrivate);
            CGEventSourceSetUserData(_mouseEventSource, DeviceId);
            CGEventSourceSetUserData(_tabletEventSource, DeviceId);
            _mouseEvent = IntPtr.Zero;
            _keyboard = DesktopInterop.VirtualKeyboard as MacOSVirtualKeyboard
                        ?? throw new InvalidOperationException("Could not get virtual keyboard");
            _windowActivator = new MacOSWindowActivator();
        }

        public void MouseDown(MouseButton button)
        {
            LogTiming($"MouseDown button={button}");
            if (!_pendingX.HasValue)
                QueuePendingPositionFromSystem();
            SetButtonState(ref _currButtonStates, ToCGMouseButton(button), true);
        }

        public void MouseUp(MouseButton button)
        {
            LogTiming($"MouseUp button={button}");
            if (!_pendingX.HasValue)
                QueuePendingPositionFromSystem();
            SetButtonState(ref _currButtonStates, ToCGMouseButton(button), false);
        }

        public void ScrollVertically(int amount)
        {
            _scrollDeltaX = -amount;
        }

        public void ScrollHorizontally(int amount)
        {
            _scrollDeltaY = -amount;
        }

        public void Flush()
        {
            if (_currButtonStates != _prevButtonStates)
            {
                // keys here just changed, no drag event should be sent
                ProcessKeyStates(_prevButtonStates, _currButtonStates);
                _prevButtonStates = _currButtonStates;
            }
            else if (DrainPendingPosition() is { } position)
            {
                // can send drag here
                var lastButtonSet = IsButtonSet(_currButtonStates, _lastButton);
                var cgEventType = ToDragCGEventType(_lastButton, lastButtonSet);
                ResetMouseEvent(cgEventType, _lastButton);
                SetPendingPosition(_mouseEvent, position.X, position.Y);
                var location = CGEventGetLocation(_mouseEvent);
                _mouseEventTargetKind = ResolveTargetKind(location);
                if (_currButtonStates == 0)
                    _windowActivator.QueueTargetUpdate(location);
                ApplyMouseEventValues();
                PostEvent();
            }
            if (_scrollDeltaX.HasValue || _scrollDeltaY.HasValue)
            {
                var scrollEventRef = CGEventCreateScrollWheelEvent2(_mouseEventSource, CGScrollEventUnit.kCGScrollEventUnitPixel, 2, _scrollDeltaX ?? 0, _scrollDeltaY ?? 0, 0);
                ApplyEventFlags(scrollEventRef);
                CGEventPost(CGEventTapLocation.kCGHIDEventTap, scrollEventRef);
                CFRelease(scrollEventRef);
                _scrollDeltaX = _scrollDeltaY = null;
            }
        }

        public void Reset()
        {
            // send a key up for all currently held keys
            if (_currButtonStates > 0)
            {
                var previousButtonStates = _currButtonStates;
                _currButtonStates = 0;
                ProcessKeyStates(previousButtonStates, 0);
                _prevButtonStates = 0;
            }
        }

        public void SetEraser(bool isEraser)
        {
            if (_isEraser.HasValue && _isEraser.Value == isEraser)
                return;

            _isEraser = isEraser;

            // Immediately post a proximity event to notify applications of the tool change,
            // mirroring how Linux EvdevVirtualTablet sends the tool switch event right away.
            PostProximityEvent();

            // Reset the stopwatch so ApplyTabletValues doesn't redundantly re-send
            // a proximity event on the next frame.
            _stopWatch.Restart();
        }

        public void SetTilt(Vector2 tilt)
        {
            _tilt = tilt;
        }

        protected abstract void SetPendingPosition(IntPtr mouseEvent, float x, float y);
        protected abstract void ResetPendingPosition(IntPtr mouseEvent);

        // binding can be triggered by auxiliary buttons and cursor might be moved by other devices.
        // in such case we fetch the position from system.
        protected abstract void QueuePendingPositionFromSystem();

        protected void QueuePendingPosition(float x, float y)
        {
            _pendingX = x;
            _pendingY = y;
            if (Vector2.Distance(_lastMouseDownPosition, new Vector2(x, y)) > DoubleClickMoveTolerance)
            {
                _mouseMovedSinceLastDown = true;
            }
        }

        public void SetPressure(float percentage)
        {
            _pressure = percentage;
        }

        private void PostProximityEvent()
        {
            var pointerType = _isEraser ?? false
                ? NSPointingDeviceType.Eraser
                : NSPointingDeviceType.Pen;

            var proximityEvent = CGEventCreate(_tabletEventSource);
            CGEventSetType(proximityEvent, CGEventType.kCGEventTabletProximity);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventEnterProximity, 1);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventPointerType, (long)pointerType);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventCapabilityMask, (long)TabletCapabilityMask);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventDeviceID, DeviceId);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventVendorPointerType, VendorPointerType);

            CGEventPost(CGEventTapLocation.kCGHIDEventTap, proximityEvent);
            CFRelease(proximityEvent);
        }

        private Vector2? DrainPendingPosition()
        {
            if (_pendingX.HasValue && _pendingY.HasValue)
            {
                var vector2 = new Vector2(_pendingX.Value, _pendingY.Value);
                _pendingX = null;
                _pendingY = null;
                return vector2;
            }

            return null;
        }

        private void ProcessKeyStates(int prevButtonStates, int currButtonStates)
        {
            for (int i = 0; i < 5; i++)
            {
                var button = (CGMouseButton)i;
                var currState = IsButtonSet(currButtonStates, button);
                var prevState = IsButtonSet(prevButtonStates, button);

                if (currState != prevState)
                {
                    var doubleClickInvalidated = _mouseMovedSinceLastDown || _doubleClickStopWatch.ElapsedMilliseconds > _doubleClickIntervalInMs;

                    if (currState)
                    {
                        if (_pendingX.HasValue && (_clickState == 0 || doubleClickInvalidated))
                        {
                            _doubleClickStopWatch.Restart();
                            _lastMouseDownPosition = new Vector2(_pendingX.Value, _pendingY!.Value);
                            _mouseMovedSinceLastDown = false;
                            _clickState = 1;
                        }
                        else
                        {
                            _clickState++;
                        }
                    }
                    else
                    {
                        if (doubleClickInvalidated)
                        {
                            _clickState = 0;
                        }
                    }

                    var cgEventType = ToNoDragCGEventType(button, currState);
                    ResetMouseEvent(cgEventType, button);
                    ResetPendingPosition(_mouseEvent);
                    if (DrainPendingPosition() is { } position)
                    {
                        SetPendingPosition(_mouseEvent, position.X, position.Y);
                    }

                    var location = CGEventGetLocation(_mouseEvent);
                    _mouseEventTargetKind = ResolveTargetKind(location);
                    if (currState)
                    {
                        _windowActivator.QueueTargetUpdate(location);
                        if (!_forcePlainMouseUntilRelease && IsPlainMouseTarget(_mouseEventTargetKind))
                        {
                            _forcePlainMouseUntilRelease = true;
                            _forcedPlainMouseTargetKind = _mouseEventTargetKind;
                        }

                        _mouseEventTargetKind = ResolveTargetKind(location);
                    }
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.mouseEventButtonNumber, i);
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.mouseEventClickState, _clickState); // clickState should be set to 1 (or more) during up, down, and drag events
                    ApplyMouseEventValues();
                    PostEvent();
                    if (!currState && currButtonStates == 0)
                    {
                        _forcePlainMouseUntilRelease = false;
                        _forcedPlainMouseTargetKind = MacOSPointerTargetKind.Unknown;
                    }
                    _lastButton = button;
                }
            }

            if (currButtonStates == 0)
            {
                // no buttons are pressed, reset button to 0
                if (_mouseEvent != IntPtr.Zero)
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.mouseEventButtonNumber, 0);
            }
        }

        private static CGMouseButton ToCGMouseButton(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => CGMouseButton.kCGMouseButtonLeft,
                MouseButton.Right => CGMouseButton.kCGMouseButtonRight,
                MouseButton.Middle => CGMouseButton.kCGMouseButtonCenter,
                MouseButton.Backward => CGMouseButton.kCGMouseButtonBackward,
                MouseButton.Forward => CGMouseButton.kCGMouseButtonForward,
                _ => throw new ArgumentException("Invalid mouse button", nameof(button))
            };
        }

        private static CGEventType ToNoDragCGEventType(CGMouseButton button, bool state)
        {
            return (button, state) switch
            {
                (CGMouseButton.kCGMouseButtonLeft, true) => CGEventType.kCGEventLeftMouseDown,
                (CGMouseButton.kCGMouseButtonLeft, false) => CGEventType.kCGEventLeftMouseUp,
                (CGMouseButton.kCGMouseButtonRight, true) => CGEventType.kCGEventRightMouseDown,
                (CGMouseButton.kCGMouseButtonRight, false) => CGEventType.kCGEventRightMouseUp,
                (_, true) => CGEventType.kCGEventOtherMouseDown,
                (_, false) => CGEventType.kCGEventOtherMouseUp,
            };
        }

        private static CGEventType ToDragCGEventType(CGMouseButton button, bool state)
        {
            return (button, state) switch
            {
                (CGMouseButton.kCGMouseButtonLeft, true) => CGEventType.kCGEventLeftMouseDragged,
                (CGMouseButton.kCGMouseButtonRight, true) => CGEventType.kCGEventRightMouseDragged,
                (_, true) => CGEventType.kCGEventOtherMouseDragged,
                (_, false) => CGEventType.kCGEventMouseMoved,
            };
        }

        private static bool IsButtonSet(int buttonStates, CGMouseButton button)
        {
            return (buttonStates & (1 << (int)button)) != 0;
        }

        private static void SetButtonState(ref int buttonStates, CGMouseButton button, bool state)
        {
            if (state)
                buttonStates |= 1 << (int)button;
            else
                buttonStates &= ~(1 << (int)button);
        }

        private void ApplyMouseEventValues()
        {
            var pressure = _pressure.GetValueOrDefault(0f);
            CGEventSetDoubleValueField(_mouseEvent, CGEventField.mouseEventPressure, pressure);
            CGEventSetDoubleValueField(_mouseEvent, CGEventField.tabletEventPointPressure, pressure);
            CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletEventPointButtons, MacOSMouseEventPolicy.GetTabletButtons(_currButtonStates));
            CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletEventDeviceID, DeviceId);
            if (_tilt != null)
            {
                CGEventSetDoubleValueField(_mouseEvent, CGEventField.tabletEventTiltX, _tilt.Value.X / 90.0);
                CGEventSetDoubleValueField(_mouseEvent, CGEventField.tabletEventTiltY, -_tilt.Value.Y / 90.0);
            }
            ApplyEventFlags(_mouseEvent);
        }

        private void ApplyPressureMouseEventValues(IntPtr mouseEvent)
        {
            var location = CGEventGetLocation(mouseEvent);
            var pressure = _pressure.GetValueOrDefault(0f);
            CGEventSetIntegerValueField(mouseEvent, CGEventField.mouseEventSubtype, (long)CGMouseEventSubtype.TabletPoint);
            CGEventSetDoubleValueField(mouseEvent, CGEventField.mouseEventPressure, pressure);
            CGEventSetDoubleValueField(mouseEvent, CGEventField.tabletEventPointPressure, pressure);
            CGEventSetIntegerValueField(mouseEvent, CGEventField.tabletEventPointX, (long)Math.Round(location.x));
            CGEventSetIntegerValueField(mouseEvent, CGEventField.tabletEventPointY, (long)Math.Round(location.y));
            CGEventSetIntegerValueField(mouseEvent, CGEventField.tabletEventPointButtons, MacOSMouseEventPolicy.GetTabletButtons(_currButtonStates));
            CGEventSetIntegerValueField(mouseEvent, CGEventField.tabletEventDeviceID, DeviceId);
            if (_tilt != null)
            {
                CGEventSetDoubleValueField(mouseEvent, CGEventField.tabletEventTiltX, _tilt.Value.X / 90.0);
                CGEventSetDoubleValueField(mouseEvent, CGEventField.tabletEventTiltY, -_tilt.Value.Y / 90.0);
            }
            ApplyEventFlags(mouseEvent);
        }

        private void PostTabletValues(CGPoint location)
        {
            long elapsed = 0;
            if (_currButtonStates == 0 && _prevButtonStates == 0)
            {
                elapsed = _stopWatch.ElapsedMilliseconds;
                _stopWatch.Restart();
            }

            var semantics = MacOSMouseEventPolicy.Create(_currButtonStates, _prevButtonStates, elapsed);
            if (semantics.PostProximityEvent)
                PostProximityEvent();

            if (!semantics.PostTabletPointEvent)
                return;

            var tabletEvent = CGEventCreate(_tabletEventSource);
            CGEventSetType(tabletEvent, CGEventType.kCGEventTabletPointer);
            CGEventSetLocation(tabletEvent, location);
            CGEventSetIntegerValueField(tabletEvent, CGEventField.tabletEventPointButtons, semantics.TabletButtons);
            CGEventSetIntegerValueField(tabletEvent, CGEventField.tabletEventDeviceID, DeviceId);
            CGEventSetDoubleValueField(tabletEvent, CGEventField.tabletEventPointPressure, _pressure ?? 1.0);

            if (_tilt != null)
            {
                CGEventSetDoubleValueField(tabletEvent, CGEventField.tabletEventTiltX, _tilt.Value.X / 90.0);
                // TiltY is inverted on MacOS
                // see https://github.com/chromium/chromium/blob/62f1a92b04c1172431a64d581be9e64742c81576/content/browser/renderer_host/input/web_input_event_builders_mac.mm#L431
                CGEventSetDoubleValueField(tabletEvent, CGEventField.tabletEventTiltY, -_tilt.Value.Y / 90.0);
            }

            ApplyEventFlags(tabletEvent);
            CGEventPost(CGEventTapLocation.kCGHIDEventTap, tabletEvent);
            CFRelease(tabletEvent);
        }

        private void ApplyEventFlags(IntPtr eventRef)
        {
            // This uses an undocumented flag that tells the system to automatically include the event flags in the event.
            // It's a better approach than fetching the active event flags ourselves, as doing so can introduce a race condition
            // (e.g., if a modifier key is released after we check its status but before the event is posted).
            // However, this flag has not effects for synthetic keyboard events, so we manually set the flags if there are modifiers bindings.

            if (_keyboard.getCurrentFlags() != 0)
                CGEventSetFlags(eventRef, _keyboard.getCurrentFlags());
            else
                CGEventSetFlags(eventRef, ~0U);
        }

        private void PostEvent()
        {
            var location = CGEventGetLocation(_mouseEvent);
            if (ShouldWarpCursor(_mouseEventType))
                _ = CGWarpMouseCursorPosition(location);
            LogTiming($"CGEventPost type={_mouseEventType} target={_mouseEventTargetKind} pressure={_pressure.GetValueOrDefault(0f):0.###} buttons=0x{_currButtonStates:X}");
            CGEventPost(CGEventTapLocation.kCGHIDEventTap, _mouseEvent);
            // Fields in a CGEvent are stored in a union determined by the event type,
            // and they cannot be safely reused.
            CFRelease(_mouseEvent);
            _mouseEvent = IntPtr.Zero;
            PostTabletValues(location);
        }

        private void ResetMouseEvent(CGEventType eventType, CGMouseButton button)
        {
            if (_mouseEvent != IntPtr.Zero)
                CFRelease(_mouseEvent);

            _mouseEventType = eventType;
            _mouseEventButton = button;
            _mouseEvent = CGEventCreateMouseEvent(_mouseEventSource, eventType, new CGPoint(0, 0), button);
            _mouseEventTargetKind = MacOSPointerTargetKind.Unknown;
        }

        private void PostPressureMouseEvent(CGPoint location)
        {
            var pressureEvent = CGEventCreateMouseEvent(_mouseEventSource, _mouseEventType, location, _mouseEventButton);
            CGEventSetIntegerValueField(pressureEvent, CGEventField.mouseEventButtonNumber, (long)_mouseEventButton);
            CGEventSetIntegerValueField(pressureEvent, CGEventField.mouseEventClickState, _clickState);
            ApplyPressureMouseEventValues(pressureEvent);
            LogTiming($"CGEventPost pressure-shadow type={_mouseEventType} subtype={CGMouseEventSubtype.TabletPoint} target={_mouseEventTargetKind} pressure={_pressure.GetValueOrDefault(0f):0.###} buttons=0x{_currButtonStates:X}");
            CGEventPost(CGEventTapLocation.kCGHIDEventTap, pressureEvent);
            CFRelease(pressureEvent);
        }

        private MacOSPointerTargetKind ResolveTargetKind(CGPoint location) =>
            _forcePlainMouseUntilRelease ? _forcedPlainMouseTargetKind : _windowActivator.GetCachedTargetKind(location);

        private static bool IsPlainMouseTarget(MacOSPointerTargetKind targetKind) =>
            targetKind is MacOSPointerTargetKind.BackgroundApplication or MacOSPointerTargetKind.SystemUi;

        private static bool ShouldWarpCursor(CGEventType eventType)
        {
            return IsPointerMotionEvent(eventType);
        }

        private static bool IsPointerMotionEvent(CGEventType eventType)
        {
            return eventType is CGEventType.kCGEventMouseMoved or
                                CGEventType.kCGEventLeftMouseDragged or
                                CGEventType.kCGEventRightMouseDragged or
                                CGEventType.kCGEventOtherMouseDragged;
        }

        private static void LogTiming(string message)
        {
            if (Interlocked.Increment(ref _timingLogs) <= 64)
                Log.Debug("WH851 Timing", $"{Stopwatch.GetTimestamp()} {message}");
        }

        ~MacOSVirtualMouse()
        {
            if (_mouseEventSource != IntPtr.Zero)
            {
                CFRelease(_mouseEventSource);
            }
            if (_tabletEventSource != IntPtr.Zero)
            {
                CFRelease(_tabletEventSource);
            }
            if (_mouseEvent != IntPtr.Zero)
            {
                CFRelease(_mouseEvent);
            }
            _windowActivator?.Dispose();
        }
    }
}
