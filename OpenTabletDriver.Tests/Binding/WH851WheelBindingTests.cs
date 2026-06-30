using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Configurations.Parsers.Huion;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
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
        public void WH851ConfiguresNineHumanNumberedAuxiliaryButtons()
        {
            var configuration = GetWH851Configuration();

            Assert.Equal(9u, configuration.Specifications.AuxiliaryButtons!.ButtonCount);
        }

        [Fact]
        public void WH851ClaimsBothUsbAndBluetoothIdentifiers()
        {
            var configuration = GetWH851Configuration();

            // Bluetooth (0x8251, 10-byte) and USB cable (0x2003, 12-byte) both route to the
            // same parser, giving identical downstream behavior across transports.
            Assert.Contains(configuration.DigitizerIdentifiers, identifier => identifier.ProductID == 33361 && identifier.InputReportLength == 10);
            Assert.Contains(configuration.DigitizerIdentifiers, identifier => identifier.ProductID == 8195 && identifier.InputReportLength == 12);
            Assert.All(configuration.DigitizerIdentifiers, identifier =>
                Assert.Equal("OpenTabletDriver.Configurations.Parsers.Huion.WH851BluetoothReportParser", identifier.ReportParser));
        }

        [Fact]
        public void WH851DefaultsBindCenterButtonAndWheelModeActions()
        {
            var configuration = GetWH851Configuration();
            var settings = BindingSettings.GetDefaults(configuration.Specifications);

            settings.ApplyTabletSpecificDefaults(configuration.Name);

            Assert.Equal(9, settings.AuxButtons.Count);
            Assert.Equal(typeof(WheelModeSwitchBinding).FullName, settings.AuxButtons[8]!.Path);
            Assert.Equal(typeof(WheelModeClockwiseBinding).FullName, settings.WheelBindings[0].ClockwiseRotation!.Path);
            Assert.Equal(typeof(WheelModeCounterClockwiseBinding).FullName, settings.WheelBindings[0].CounterClockwiseRotation!.Path);
            Assert.Contains(settings.WheelBindings[0].ClockwiseRotation!.Settings, setting => setting.Property == nameof(WheelModeActionBinding.ScrollAmount));
            Assert.Contains(settings.WheelBindings[0].ClockwiseRotation!.Settings, setting => setting.Property == nameof(WheelModeActionBinding.BrushDetentsPerStep));
            Assert.Contains(settings.WheelBindings[0].ClockwiseRotation!.Settings, setting => setting.Property == nameof(WheelModeActionBinding.ZoomDetentsPerStep));
            Assert.Contains(settings.WheelBindings[0].ClockwiseRotation!.Settings, setting => setting.Property == nameof(WheelModeActionBinding.DebounceMs));
            Assert.Equal(15, settings.WheelBindings[0].ClockwiseActivationThreshold);
            Assert.Equal(15, settings.WheelBindings[0].CounterClockwiseActivationThreshold);
        }

        [Fact]
        public void WheelModeActionsCycleBrushZoomAndScroll()
        {
            // Mode cycle order is Brush Size -> Zoom -> Scroll, starting in Brush Size.
            WheelModeSwitchBinding.ResetMode();
            var pointer = new RecordingScrollHandler();
            var keyboard = new RecordingKeyboard();
            var clockwise = new WheelModeClockwiseBinding
            {
                Pointer = pointer,
                Keyboard = keyboard,
                DebounceMs = 0
            };
            var counterClockwise = new WheelModeCounterClockwiseBinding
            {
                Pointer = pointer,
                Keyboard = keyboard,
                DebounceMs = 0
            };
            var switchBinding = new WheelModeSwitchBinding();

            // Brush Size: one detent per key press.
            clockwise.Press(null!, new DeviceReport([]));
            counterClockwise.Press(null!, new DeviceReport([]));

            // Zoom: ZoomDetentsPerStep defaults to 4, so 4 detents => one zoom key.
            switchBinding.Press(null!, new DeviceReport([]));
            for (var i = 0; i < 4; i++)
                clockwise.Press(null!, new DeviceReport([]));
            for (var i = 0; i < 4; i++)
                counterClockwise.Press(null!, new DeviceReport([]));

            // Scroll: one detent per flush.
            switchBinding.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));

            Assert.Equal(-12, pointer.VerticalAmount);
            Assert.Equal(1, pointer.Flushes);
            Assert.Contains("press:RightBracket", keyboard.Events);
            Assert.Contains("press:LeftBracket", keyboard.Events);
            Assert.Contains(keyboard.Events, e => e is "press:Application" or "press:Control");
            Assert.Contains("press:Equal", keyboard.Events);
            Assert.Contains("press:Minus", keyboard.Events);
        }

        [Fact]
        public void WheelModeActionsDebounceDuplicateReports()
        {
            WheelModeSwitchBinding.ResetMode();
            var pointer = new RecordingScrollHandler();
            var clockwise = new WheelModeClockwiseBinding
            {
                Pointer = pointer
            };
            var switchBinding = new WheelModeSwitchBinding();

            // Cycle Brush Size -> Zoom -> Scroll to exercise scroll debounce.
            switchBinding.Press(null!, new DeviceReport([]));
            switchBinding.Press(null!, new DeviceReport([]));

            clockwise.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));

            Assert.Equal(-12, pointer.VerticalAmount);
            Assert.Equal(1, pointer.Flushes);
        }

        private static TabletConfiguration GetWH851Configuration()
        {
            return TestData.DeviceConfigurationProvider.TabletConfigurations
                .Single(config => config.Name == "Gaomon WH851");
        }

        private sealed class RecordingScrollHandler : IMouseScrollHandler, ISynchronousPointer
        {
            public int VerticalAmount { get; private set; }
            public int Flushes { get; private set; }

            public void ScrollVertically(int amount) => VerticalAmount += amount;
            public void ScrollHorizontally(int amount) { }
            public void Reset() { }
            public void Flush() => Flushes++;
        }

        private sealed class RecordingKeyboard : IVirtualKeyboard
        {
            public List<string> Events { get; } = [];
            public IEnumerable<string> SupportedKeys { get; } =
            [
                "Application",
                "Control",
                "Equal",
                "Minus",
                "RightBracket",
                "LeftBracket"
            ];

            public void Press(string key) => Events.Add($"press:{key}");
            public void Release(string key) => Events.Add($"release:{key}");
            public void Press(IEnumerable<string> keys)
            {
                foreach (var key in keys)
                    Press(key);
            }

            public void Release(IEnumerable<string> keys)
            {
                foreach (var key in keys)
                    Release(key);
            }
        }
    }
}
