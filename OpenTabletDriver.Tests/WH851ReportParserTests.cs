using OpenTabletDriver.Configurations.Parsers.Huion;
using OpenTabletDriver.Devices.MacOSHid;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public sealed class WH851ReportParserTests
    {
        [Fact]
        public void BluetoothParserParsesStandardBlePenReport()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x0a,
                0x40 | 0x02,
                0x34, 0x12,
                0x78, 0x56,
                0xbc, 0x0a,
                0xfe,
                0x05
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(0x1234, tabletReport.Position.X);
            Assert.Equal(0x5678, tabletReport.Position.Y);
            Assert.Equal(0x0abcu, tabletReport.Pressure);
            Assert.Equal(-2, tabletReport.Tilt.X);
            Assert.Equal(5, tabletReport.Tilt.Y);
            Assert.True(tabletReport.PenButtons[0]);
            Assert.False(tabletReport.PenButtons[1]);
            Assert.False(tabletReport.Eraser);
        }

        [Fact]
        public void BluetoothParserReturnsOutOfRangeWhenBleInRangeBitIsClear()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x0a,
                0x00,
                0x34, 0x12,
                0x78, 0x56,
                0xbc, 0x0a,
                0xfe,
                0x05
            ]);

            Assert.IsType<OutOfRangeReport>(report);
        }

        [Fact]
        public void BluetoothParserAppliesMinimumPressureForStandardBleContact()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x0a,
                0x40 | 0x01,
                0x34, 0x12,
                0x78, 0x56,
                0x00, 0x00,
                0x00,
                0x00
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(256u, tabletReport.Pressure);
        }

        [Fact]
        public void MacOSHidSyntheticTabletPointPressureSetsTouchBit()
        {
            var report = MacOSWH851HidRootHub.CreateSyntheticBluetoothPenReport(
                tabletX: 0,
                tabletY: 0,
                pressure: 1,
                buttons: 0,
                tiltX: 0,
                tiltY: 0);

            Assert.Equal(0x0a, report[0]);
            Assert.Equal(0x40 | 0x01, report[1]);
            Assert.Equal(0xff, report[6]);
            Assert.Equal(0x3f, report[7]);
        }

        [Fact]
        public void MacOSHidSyntheticTabletPointLeftButtonSetsTouchBitWithoutPressure()
        {
            var report = MacOSWH851HidRootHub.CreateSyntheticBluetoothPenReport(
                tabletX: 0,
                tabletY: 0,
                pressure: 0,
                buttons: 0x01,
                tiltX: 0,
                tiltY: 0);

            Assert.Equal(0x40 | 0x01, report[1]);
            Assert.Equal(0x00, report[6]);
            Assert.Equal(0x00, report[7]);
        }

        [Fact]
        public void MacOSHidRootHubOnlyOwnsBluetoothProduct()
        {
            Assert.True(MacOSWH851HidRootHub.IsSupportedProduct(0x8251));
            Assert.False(MacOSWH851HidRootHub.IsSupportedProduct(0x2003));
        }

        [Fact]
        public void BluetoothParserParsesOfficialBlePenReport()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                0x80 | 0x02,
                0x34, 0x12,
                0x78, 0x56,
                0xbc, 0x0a,
                0x01, 0x02,
                0xfe,
                0x05
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(0x011234, tabletReport.Position.X);
            Assert.Equal(0x025678, tabletReport.Position.Y);
            Assert.Equal(0x0abcu, tabletReport.Pressure);
            Assert.Equal(-2, tabletReport.Tilt.X);
            Assert.Equal(5, tabletReport.Tilt.Y);
            Assert.True(tabletReport.PenButtons[0]);
        }

        [Fact]
        public void BluetoothParserParsesShortOfficialBlePenReport()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                0x81,
                0x34, 0x12,
                0x78, 0x56,
                0xbc, 0x0a,
                0x01, 0x02
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(0x011234, tabletReport.Position.X);
            Assert.Equal(0x025678, tabletReport.Position.Y);
            Assert.Equal(0x0abcu, tabletReport.Pressure);
            Assert.Equal(0, tabletReport.Tilt.X);
            Assert.Equal(0, tabletReport.Tilt.Y);
        }

        [Fact]
        public void BluetoothParserReturnsOutOfRangeWhenOfficialBleInRangeBitIsClear()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                0x00,
                0x34, 0x12,
                0x78, 0x56,
                0xbc, 0x0a,
                0x01, 0x02,
                0xfe,
                0x05
            ]);

            Assert.IsType<OutOfRangeReport>(report);
        }

        [Theory]
        [InlineData(0x01, 1)]
        [InlineData(0x02, -1)]
        [InlineData(0x00, 0)]
        public void BluetoothParserParsesOfficialBleWheelReports(byte wheelData, int expectedDelta)
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                0xf1,
                0x01, 0x01,
                0x00, wheelData,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0x00
            ]);

            var wheelReport = Assert.IsAssignableFrom<IRelativeWheelReport>(report);
            Assert.Equal([expectedDelta], wheelReport.AnalogDeltas);
        }

        [Fact]
        public void BluetoothParserParsesOfficialBleAuxReport()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                0xe0,
                0x00, 0x00,
                0x01, 0x02,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0x00,
                0x00
            ]);

            var auxReport = Assert.IsAssignableFrom<IAuxReport>(report);
            Assert.True(auxReport.AuxButtons[0]);
            Assert.True(auxReport.AuxButtons[9]);
        }

        [Fact]
        public void BluetoothParserKeepsOfficialBleHoverPressureAtZero()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                0x80,
                0x34, 0x12,
                0x78, 0x56,
                0x00, 0x00,
                0x01, 0x02,
                0x00,
                0x00
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(0u, tabletReport.Pressure);
        }

        [Fact]
        public void BluetoothParserAppliesMinimumPressureForOfficialBleContact()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                0x81,
                0x34, 0x12,
                0x78, 0x56,
                0x00, 0x00,
                0x01, 0x02,
                0x00,
                0x00
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(256u, tabletReport.Pressure);
        }

        [Fact]
        public void BluetoothParserDoesNotTreatOfficialBleSecondBarrelButtonAsEraser()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                0x85,
                0x88, 0x6d,
                0x2c, 0x24,
                0x8b, 0x06,
                0x00, 0x00,
                0x11,
                0xf3
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(0x068bu, tabletReport.Pressure);
            Assert.False(tabletReport.PenButtons[0]);
            Assert.True(tabletReport.PenButtons[1]);
            Assert.False(tabletReport.Eraser);
        }

        [Theory]
        [InlineData(0x82, false, true, false, false)]
        [InlineData(0x84, false, false, true, false)]
        [InlineData(0x85, true, false, true, false)]
        public void BluetoothParserParsesOfficialBlePenButtonStatus(byte status, bool touching, bool button1, bool button2, bool eraser)
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x08,
                status,
                0x88, 0x6d,
                0x2c, 0x24,
                0x00, 0x00,
                0x00, 0x00,
                0x11,
                0xf3
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(touching ? 256u : 0u, tabletReport.Pressure);
            Assert.Equal(button1, tabletReport.PenButtons[0]);
            Assert.Equal(button2, tabletReport.PenButtons[1]);
            Assert.Equal(eraser, tabletReport.Eraser);
        }

        [Fact]
        public void BluetoothParserPreservesUnknownReports()
        {
            var parser = new WH851BluetoothReportParser();
            var raw = new byte[] { 0x99, 0x01, 0x02 };

            var report = parser.Parse(raw);

            var deviceReport = Assert.IsType<DeviceReport>(report);
            Assert.Same(raw, deviceReport.Raw);
        }

        [Fact]
        public void BluetoothParserPreservesShortBleLikeReports()
        {
            var parser = new WH851BluetoothReportParser();
            var raw = new byte[] { 0x0a, 0x40, 0x01 };

            var report = parser.Parse(raw);

            var deviceReport = Assert.IsType<DeviceReport>(report);
            Assert.Same(raw, deviceReport.Raw);
        }

        [Fact]
        public void BluetoothParserPreservesEmptyReports()
        {
            var parser = new WH851BluetoothReportParser();
            var raw = System.Array.Empty<byte>();

            var report = parser.Parse(raw);

            var deviceReport = Assert.IsType<DeviceReport>(report);
            Assert.Same(raw, deviceReport.Raw);
        }

        [Theory]
        [InlineData(0x40 | 0x02, true, false, false)]
        [InlineData(0x40 | 0x04, false, true, true)]
        [InlineData(0x40 | 0x02 | 0x04, true, true, true)]
        public void BluetoothParserParsesPenButtonsAndEraser(byte status, bool button1, bool button2, bool eraser)
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x0a,
                status,
                0x34, 0x12,
                0x78, 0x56,
                0x00, 0x00,
                0x00,
                0x00
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(button1, tabletReport.PenButtons[0]);
            Assert.Equal(button2, tabletReport.PenButtons[1]);
            Assert.Equal(eraser, tabletReport.Eraser);
        }

        [Fact]
        public void BluetoothParserParsesZeroAndMaxPressure()
        {
            var parser = new WH851BluetoothReportParser();

            var zeroPressure = Assert.IsType<WH851BluetoothPenReport>(parser.Parse(
            [
                0x0a,
                0x40,
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0x00
            ]));

            var maxPressure = Assert.IsType<WH851BluetoothPenReport>(parser.Parse(
            [
                0x0a,
                0x40,
                0x00, 0x00,
                0x00, 0x00,
                0xff, 0x3f,
                0x00,
                0x00
            ]));

            Assert.Equal(0u, zeroPressure.Pressure);
            Assert.Equal(16383u, maxPressure.Pressure);
        }

        [Fact]
        public void BluetoothParserParsesNegativeTilt()
        {
            var parser = new WH851BluetoothReportParser();

            var report = parser.Parse(
            [
                0x0a,
                0x40,
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x80,
                0xff
            ]);

            var tabletReport = Assert.IsType<WH851BluetoothPenReport>(report);
            Assert.Equal(-128, tabletReport.Tilt.X);
            Assert.Equal(-1, tabletReport.Tilt.Y);
        }

        [Fact]
        public void BluetoothParserPreservesMacOSHidControlReports()
        {
            var parser = new WH851BluetoothReportParser();
            var raw = new byte[]
            {
                0x03,
                0xf1,
                0x03,
                0x02, 0x00,
                0xff, 0xff,
                0x00, 0x00, 0x00
            };

            var report = parser.Parse(raw);

            var deviceReport = Assert.IsType<DeviceReport>(report);
            Assert.Same(raw, deviceReport.Raw);
        }
    }
}
