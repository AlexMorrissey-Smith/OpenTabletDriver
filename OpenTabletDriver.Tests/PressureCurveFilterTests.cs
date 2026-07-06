using System;
using System.Collections.Generic;
using OpenTabletDriver.Desktop.Filters;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public sealed class PressureCurveFilterTests
    {
        private const uint MaxPressure = 16383;

        [Fact]
        public void LinearControlPointsAreIdentity()
        {
            var filter = CreateFilter(0.33f, 0.33f, 0.67f, 0.67f);

            foreach (var pressure in new uint[] { 1, 100, 8191, 16000, MaxPressure })
            {
                var report = Run(filter, pressure);
                Assert.InRange((long)report.Pressure, (long)pressure - 2, (long)pressure + 2);
            }
        }

        [Fact]
        public void SoftCurveRaisesMidPressure()
        {
            var filter = CreateFilter(0.25f, 0.55f, 0.55f, 0.95f);
            var report = Run(filter, MaxPressure / 2);
            Assert.True(report.Pressure > MaxPressure / 2);
        }

        [Fact]
        public void FirmCurveLowersMidPressure()
        {
            var filter = CreateFilter(0.45f, 0.05f, 0.75f, 0.45f);
            var report = Run(filter, MaxPressure / 2);
            Assert.True(report.Pressure < MaxPressure / 2);
        }

        [Fact]
        public void EndpointsAndZeroPressureAreStable()
        {
            var filter = CreateFilter(0.25f, 0.55f, 0.55f, 0.95f);
            Assert.Equal(0u, Run(filter, 0).Pressure);
            Assert.Equal(MaxPressure, Run(filter, MaxPressure).Pressure);
        }

        [Fact]
        public void OutputIsMonotonicInInput()
        {
            var filter = CreateFilter(0.65f, 0.1f, 0.35f, 0.9f);
            uint previous = 0;
            for (uint p = 0; p <= MaxPressure; p += 512)
            {
                var output = Run(filter, p).Pressure;
                Assert.True(output >= previous, $"non-monotonic at input {p}");
                previous = output;
            }
        }

        private static PressureCurveFilter CreateFilter(float x1, float y1, float x2, float y2)
        {
            return new PressureCurveFilter
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Tablet = new TabletReference(
                    new TabletConfiguration
                    {
                        Name = "Test",
                        DigitizerIdentifiers = new List<DeviceIdentifier>(),
                        Specifications = new TabletSpecifications
                        {
                            Digitizer = new DigitizerSpecifications(),
                            Pen = new PenSpecifications { MaxPressure = MaxPressure }
                        }
                    },
                    new List<DeviceIdentifier>()
                )
            };
        }

        private static ITabletReport Run(PressureCurveFilter filter, uint pressure)
        {
            var report = new FakeReport { Pressure = pressure };
            ITabletReport? emitted = null;
            filter.Emit += r => emitted = r as ITabletReport;
            filter.Consume(report);
            Assert.NotNull(emitted);
            return emitted!;
        }

        private sealed class FakeReport : ITabletReport
        {
            public byte[] Raw { set; get; } = Array.Empty<byte>();
            public System.Numerics.Vector2 Position { set; get; }
            public uint Pressure { set; get; }
            public bool[] PenButtons { set; get; } = Array.Empty<bool>();
        }
    }
}
