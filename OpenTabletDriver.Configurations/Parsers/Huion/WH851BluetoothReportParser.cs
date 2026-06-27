using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenTabletDriver.Configurations.Parsers.UCLogic;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    internal static class WH851Report
    {
        public const uint MinimumTouchPressure = 256;
    }

    public class WH851BluetoothReportParser : IReportParser<IDeviceReport>
    {
        public IDeviceReport Parse(byte[] data)
        {
            if (data.Length >= 12 && data[0] == 0x08)
            {
                if (data[1] == 0xf1)
                    return new InspiroyRelWheelReport(data);

                if (data[1] == 0xe0 || data[1] == 0xe3)
                    return new UCLogicAuxReport(data);

                if (!data[1].IsBitSet(7))
                    return new OutOfRangeReport(data);

                return new WH851BluetoothPenReport(data);
            }

            if (data.Length >= 10 && data[0] == 0x0a)
            {
                if (data[1] == 0xf1)
                    return new InspiroyRelWheelReport(data);

                if (!data[1].IsBitSet(6))
                    return new OutOfRangeReport(data);

                return new WH851BluetoothPenReport(data);
            }

            return new DeviceReport(data);
        }
    }

    public struct WH851BluetoothPenReport : ITabletReport, ITiltReport, IEraserReport
    {
        public WH851BluetoothPenReport(byte[] report)
        {
            Raw = report;
            var isOfficialBleReport = report[0] == 0x08 && report.Length >= 12;
            var touching = report[1].IsBitSet(0);
            Position = new Vector2
            {
                X = ReadCoordinate(report, 2, isOfficialBleReport ? 8 : -1),
                Y = ReadCoordinate(report, 4, isOfficialBleReport ? 9 : -1)
            };
            Pressure = Unsafe.ReadUnaligned<ushort>(ref report[6]);
            if (touching && Pressure < WH851Report.MinimumTouchPressure)
                Pressure = WH851Report.MinimumTouchPressure;
            Tilt = new Vector2
            {
                X = (sbyte)report[isOfficialBleReport ? 10 : 8],
                Y = (sbyte)report[isOfficialBleReport ? 11 : 9]
            };

            PenButtons =
            [
                report[1].IsBitSet(1),
                report[1].IsBitSet(2)
            ];
            Eraser = !isOfficialBleReport && report[1].IsBitSet(2);
        }

        private static int ReadCoordinate(byte[] report, int lowOffset, int highOffset)
        {
            var value = Unsafe.ReadUnaligned<ushort>(ref report[lowOffset]);
            return highOffset >= 0 ? value | (report[highOffset] << 16) : value;
        }

        public byte[] Raw { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Tilt { get; set; }
        public uint Pressure { get; set; }
        public bool[] PenButtons { get; set; }
        public bool Eraser { get; set; }
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class WH851UsbReportParser : IReportParser<IDeviceReport>
    {
        public IDeviceReport Parse(byte[] data)
        {
            switch (data[1])
            {
                case 0xe0:
                    return new UCLogicAuxReport(data);
                case 0xe3:
                    // Group buttons, no way to use them properly for now
                    return new UCLogicAuxReport(data);
                case 0xf1:
                    return new InspiroyRelWheelReport(data);
                case 0x00:
                    return new OutOfRangeReport(data);
            }

            if (data[1].IsBitSet(7))
                return new WH851UsbPenReport(data);

            return new DeviceReport(data);
        }
    }

    public struct WH851UsbPenReport : ITabletReport, ITiltReport
    {
        public WH851UsbPenReport(byte[] report)
        {
            Raw = report;

            Position = new Vector2
            {
                X = Unsafe.ReadUnaligned<ushort>(ref report[2]),
                Y = Unsafe.ReadUnaligned<ushort>(ref report[4])
            };
            Tilt = new Vector2
            {
                X = (sbyte)report[10],
                Y = -(sbyte)report[11]
            };
            Pressure = Unsafe.ReadUnaligned<ushort>(ref report[6]);
            if (report[1].IsBitSet(0) && Pressure < WH851Report.MinimumTouchPressure)
                Pressure = WH851Report.MinimumTouchPressure;

            PenButtons =
            [
                report[1].IsBitSet(1),
                report[1].IsBitSet(2),
                report[1].IsBitSet(3),
            ];
        }

        public byte[] Raw { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Tilt { get; set; }
        public uint Pressure { get; set; }
        public bool[] PenButtons { get; set; }
    }
}
