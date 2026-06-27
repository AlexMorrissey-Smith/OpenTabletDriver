using System.Linq;
using OpenTabletDriver.Configurations.Parsers.Huion;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Tests.ConfigurationTest;
using Xunit;

namespace OpenTabletDriver.Tests.Binding
{
    public sealed class WH851MouseSemanticsTests
    {
        [Fact]
        public void BluetoothZeroPressureContactTriggersTipBinding()
        {
            var binding = new CountingBinding();
            var bindingHandler = CreateWH851BindingHandler(binding);
            var parser = new WH851BluetoothReportParser();

            bindingHandler.HandleBinding(parser.Parse(CreateOfficialBleContactReport()));

            Assert.Equal(1, binding.Presses);
            Assert.Equal(0, binding.Releases);
        }

        [Fact]
        public void BluetoothOutOfRangeReleasesHeldTipBinding()
        {
            var binding = new CountingBinding();
            var bindingHandler = CreateWH851BindingHandler(binding);
            var parser = new WH851BluetoothReportParser();

            bindingHandler.HandleBinding(parser.Parse(CreateOfficialBleContactReport()));
            bindingHandler.HandleBinding(parser.Parse(CreateOfficialBleOutOfRangeReport()));

            Assert.Equal(1, binding.Presses);
            Assert.Equal(1, binding.Releases);
        }

        private static BindingHandler CreateWH851BindingHandler(IBinding binding)
        {
            var configuration = TestData.DeviceConfigurationProvider.TabletConfigurations
                .Single(config => config.Name == "Gaomon WH851");

            return new BindingHandler(new TabletReference(configuration, configuration.DigitizerIdentifiers))
            {
                Tip = new ThresholdBindingState
                {
                    Binding = binding,
                    ActivationThreshold = 1
                }
            };
        }

        private static byte[] CreateOfficialBleContactReport() =>
        [
            0x08,
            0x81,
            0x34, 0x12,
            0x78, 0x56,
            0x00, 0x00,
            0x01, 0x02,
            0x00,
            0x00
        ];

        private static byte[] CreateOfficialBleOutOfRangeReport() =>
        [
            0x08,
            0x00,
            0x34, 0x12,
            0x78, 0x56,
            0x00, 0x00,
            0x01, 0x02,
            0x00,
            0x00
        ];

        private sealed class CountingBinding : IStateBinding
        {
            public int Presses { get; private set; }
            public int Releases { get; private set; }

            public void Press(TabletReference tablet, IDeviceReport report)
            {
                Presses++;
            }

            public void Release(TabletReference tablet, IDeviceReport report)
            {
                Releases++;
            }
        }
    }
}
