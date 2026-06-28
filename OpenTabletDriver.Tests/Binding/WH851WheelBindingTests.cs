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
        public void WH851DoesNotClaimUsbIdentifier()
        {
            var configuration = GetWH851Configuration();

            Assert.DoesNotContain(configuration.DigitizerIdentifiers, identifier => identifier.InputReportLength == 12);
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
        public void WheelModeActionsCycleScrollBrushAndZoom()
        {
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

            clockwise.Press(null!, new DeviceReport([]));
            switchBinding.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));
            counterClockwise.Press(null!, new DeviceReport([]));
            counterClockwise.Press(null!, new DeviceReport([]));
            counterClockwise.Press(null!, new DeviceReport([]));
            counterClockwise.Press(null!, new DeviceReport([]));
            switchBinding.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));
            clockwise.Press(null!, new DeviceReport([]));

            Assert.Equal(-12, pointer.VerticalAmount);
            Assert.Equal(1, pointer.Flushes);
            Assert.Contains("press:RightBracket", keyboard.Events);
            Assert.Contains("press:LeftBracket", keyboard.Events);
            Assert.Contains(keyboard.Events, e => e is "press:Application" or "press:Control");
            Assert.Contains("press:Equal", keyboard.Events);
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
