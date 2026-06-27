using System.Linq;
using OpenTabletDriver.Configurations.Parsers.Huion;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Tests.ConfigurationTest;
using Xunit;

namespace OpenTabletDriver.Tests.Binding
{
    public sealed class WH851WheelBindingTests
    {
        [Fact]
        public void WH851ConfiguresOneTwentyFourStepRelativeWheel()
        {
            var configuration = GetWH851Configuration();
            var wheel = configuration.Specifications.Wheels!.Single();

            Assert.Equal(24u, wheel.RelativeWheelSteps!.Value);
            Assert.Null(wheel.AbsoluteWheelMax);
            Assert.Equal(24u, wheel.StepCount!.Value);
            Assert.Equal(0u, wheel.ButtonCount);
        }

        [Fact]
        public void WH851RawWheelReportInvokesClockwiseBinding()
        {
            var parser = new WH851UsbReportParser();
            var bindingHandler = CreateWH851BindingHandler();
            var binding = new CountingBinding();
            bindingHandler.Wheels[0].ClockwiseRotation = new DeltaThresholdBindingState
            {
                Binding = binding,
                ActivationThreshold = 15,
                IsNegativeThreshold = false
            };

            bindingHandler.HandleBinding(parser.Parse(CreateWH851WheelReport(0x01)));

            Assert.Equal(1, binding.Presses);
        }

        [Fact]
        public void WH851RawWheelReportInvokesCounterClockwiseBinding()
        {
            var parser = new WH851UsbReportParser();
            var bindingHandler = CreateWH851BindingHandler();
            var binding = new CountingBinding();
            bindingHandler.Wheels[0].CounterClockwiseRotation = new DeltaThresholdBindingState
            {
                Binding = binding,
                ActivationThreshold = 15,
                IsNegativeThreshold = true
            };

            bindingHandler.HandleBinding(parser.Parse(CreateWH851WheelReport(0x02)));

            Assert.Equal(1, binding.Presses);
        }

        private static TabletConfiguration GetWH851Configuration()
        {
            return TestData.DeviceConfigurationProvider.TabletConfigurations
                .Single(config => config.Name == "Gaomon WH851");
        }

        private static BindingHandler CreateWH851BindingHandler()
        {
            var configuration = GetWH851Configuration();

            return new BindingHandler(new TabletReference(configuration, configuration.DigitizerIdentifiers));
        }

        private static byte[] CreateWH851WheelReport(byte wheelData) =>
        [
            0x08,
            0xf1,
            0x01, 0x01,
            0x00, wheelData,
            0x00, 0x00,
            0x00, 0x00,
            0x00,
            0x00
        ];

        private sealed class CountingBinding : IStateBinding
        {
            public int Presses { get; private set; }

            public void Press(TabletReference tablet, IDeviceReport report)
            {
                Presses++;
            }

            public void Release(TabletReference tablet, IDeviceReport report)
            {
            }
        }
    }
}
