using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests.Binding
{
    public sealed class MouseScrollDirectionSwitchBindingTests
    {
        [Fact]
        public void SwitchableMouseScrollUsesVerticalDirectionByDefault()
        {
            MouseScrollDirectionSwitchBinding.ResetDirection();
            var pointer = new RecordingScrollHandler();
            var binding = CreateSwitchableScrollBinding(pointer);

            binding.Scroll();

            Assert.Equal(-120, pointer.VerticalAmount);
            Assert.Equal(0, pointer.HorizontalAmount);
            Assert.Equal(1, pointer.Flushes);
        }

        [Fact]
        public void SwitchBindingTogglesSwitchableMouseScrollDirection()
        {
            MouseScrollDirectionSwitchBinding.ResetDirection();
            var pointer = new RecordingScrollHandler();
            var scrollBinding = CreateSwitchableScrollBinding(pointer);
            var switchBinding = new MouseScrollDirectionSwitchBinding();

            switchBinding.Press(null!, new DeviceReport([]));
            scrollBinding.Scroll();

            Assert.Equal(0, pointer.VerticalAmount);
            Assert.Equal(-120, pointer.HorizontalAmount);
            Assert.Equal(1, pointer.Flushes);
        }

        [Fact]
        public void SwitchBindingTogglesBackToVerticalDirection()
        {
            MouseScrollDirectionSwitchBinding.ResetDirection();
            var pointer = new RecordingScrollHandler();
            var scrollBinding = CreateSwitchableScrollBinding(pointer);
            var switchBinding = new MouseScrollDirectionSwitchBinding();

            switchBinding.Press(null!, new DeviceReport([]));
            switchBinding.Press(null!, new DeviceReport([]));
            scrollBinding.Scroll();

            Assert.Equal(-120, pointer.VerticalAmount);
            Assert.Equal(0, pointer.HorizontalAmount);
        }

        private static MouseScrollBinding CreateSwitchableScrollBinding(IMouseScrollHandler pointer) =>
            new()
            {
                Pointer = pointer,
                Direction = ScrollDirection.Switchable.ToString(),
                Amount = 120
            };

        private sealed class RecordingScrollHandler : IMouseScrollHandler, ISynchronousPointer
        {
            public int VerticalAmount { get; private set; }
            public int HorizontalAmount { get; private set; }
            public int Flushes { get; private set; }

            public void ScrollVertically(int amount) => VerticalAmount += amount;
            public void ScrollHorizontally(int amount) => HorizontalAmount += amount;
            public void Reset() { }
            public void Flush() => Flushes++;
        }
    }
}
