using OpenTabletDriver.Desktop.Binding;
using Xunit;

namespace OpenTabletDriver.Tests.Binding
{
    public sealed class PenScrollBindingTests
    {
        [Fact]
        public void FractionalMovementAccumulatesUntilOneTick()
        {
            float acc = 0;
            // 0.4 px * 1.0 sensitivity → no tick yet
            Assert.Equal(0, PenScrollBinding.AccumulateScroll(ref acc, 0.4f, 1.0f));
            // another 0.4 → 0.8, still none
            Assert.Equal(0, PenScrollBinding.AccumulateScroll(ref acc, 0.4f, 1.0f));
            // another 0.4 → 1.2 → one tick, 0.2 remainder carries
            Assert.Equal(1, PenScrollBinding.AccumulateScroll(ref acc, 0.4f, 1.0f));
            Assert.Equal(0.2f, acc, 5);
        }

        [Fact]
        public void NegativeDeltaScrollsOppositeDirection()
        {
            float acc = 0;
            Assert.Equal(-2, PenScrollBinding.AccumulateScroll(ref acc, -2.0f, 1.0f));
        }

        [Fact]
        public void SensitivityScalesOutput()
        {
            float acc = 0;
            // 2 px * 3.0 sensitivity = 6 ticks
            Assert.Equal(6, PenScrollBinding.AccumulateScroll(ref acc, 2.0f, 3.0f));
        }
    }
}
