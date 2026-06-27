using OpenTabletDriver.Native.OSX.Input;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    public readonly record struct MacOSMouseEventSemantics(
        bool ApplyTabletSubtypeToMouseEvent,
        bool PostTabletPointEvent,
        bool PostProximityEvent,
        int TabletButtons
    );

    public static class MacOSMouseEventPolicy
    {
        public const int ProximityExpiresDurationInMs = 200;

        public static MacOSMouseEventSemantics Create(int currentButtonStates, int previousButtonStates, long elapsedSinceLastProximityMs)
        {
            return new MacOSMouseEventSemantics(
                ApplyTabletSubtypeToMouseEvent: false,
                PostTabletPointEvent: true,
                PostProximityEvent: currentButtonStates == 0 &&
                                    previousButtonStates == 0 &&
                                    elapsedSinceLastProximityMs > ProximityExpiresDurationInMs,
                TabletButtons: GetTabletButtons(currentButtonStates)
            );
        }

        public static int GetButtonStateMask(CGMouseButton button) => 1 << (int)button;

        public static int GetTabletButtons(int buttonStates)
        {
            if ((buttonStates & GetButtonStateMask(CGMouseButton.kCGMouseButtonLeft)) != 0)
                return 1;
            if ((buttonStates & GetButtonStateMask(CGMouseButton.kCGMouseButtonRight)) != 0)
                return 2;
            if ((buttonStates & GetButtonStateMask(CGMouseButton.kCGMouseButtonCenter)) != 0)
                return 4;

            return 0;
        }
    }
}
