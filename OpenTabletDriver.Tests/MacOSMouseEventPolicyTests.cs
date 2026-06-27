using OpenTabletDriver.Desktop.Interop.Input;
using OpenTabletDriver.Native.OSX.Input;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public sealed class MacOSMouseEventPolicyTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(0, 1)]
        public void MouseEventsRemainPlainMouseEvents(int currentButtonStates, int previousButtonStates)
        {
            var semantics = MacOSMouseEventPolicy.Create(
                currentButtonStates,
                previousButtonStates,
                elapsedSinceLastProximityMs: 0);

            Assert.False(semantics.ApplyTabletSubtypeToMouseEvent);
            Assert.True(semantics.PostTabletPointEvent);
        }

        [Fact]
        public void IdlePlainMouseMoveCanAlsoPostProximityMetadata()
        {
            var semantics = MacOSMouseEventPolicy.Create(
                currentButtonStates: 0,
                previousButtonStates: 0,
                elapsedSinceLastProximityMs: MacOSMouseEventPolicy.ProximityExpiresDurationInMs + 1);

            Assert.False(semantics.ApplyTabletSubtypeToMouseEvent);
            Assert.True(semantics.PostTabletPointEvent);
            Assert.True(semantics.PostProximityEvent);
        }

        [Theory]
        [InlineData(CGMouseButton.kCGMouseButtonLeft, 1)]
        [InlineData(CGMouseButton.kCGMouseButtonRight, 2)]
        [InlineData(CGMouseButton.kCGMouseButtonCenter, 4)]
        public void TabletMetadataKeepsPressedButtonState(CGMouseButton button, int expectedTabletButtons)
        {
            var semantics = MacOSMouseEventPolicy.Create(
                MacOSMouseEventPolicy.GetButtonStateMask(button),
                previousButtonStates: 0,
                elapsedSinceLastProximityMs: 0);

            Assert.Equal(expectedTabletButtons, semantics.TabletButtons);
        }
    }
}
