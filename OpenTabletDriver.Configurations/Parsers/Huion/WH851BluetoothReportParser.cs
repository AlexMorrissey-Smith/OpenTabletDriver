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
            if (data.Length >= 10 && data[0] == 0x08)
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
            var isOfficialBleReport = report[0] == 0x08;
            var touching = report[1].IsBitSet(0);
            // Coordinates are 16-bit LE for both transports (MaxX 40640 = 0x9E40 fits in a ushort).
            // Bytes [8]/[9] are reserved (always 0 in captures) — no 24-bit extension.
            Position = new Vector2
            {
                X = Unsafe.ReadUnaligned<ushort>(ref report[2]),
                Y = Unsafe.ReadUnaligned<ushort>(ref report[4])
            };
            Pressure = Unsafe.ReadUnaligned<ushort>(ref report[6]);
            if (touching && Pressure < WH851Report.MinimumTouchPressure)
                Pressure = WH851Report.MinimumTouchPressure;
            Tilt = new Vector2
            {
                X = (sbyte)(isOfficialBleReport ? (report.Length > 10 ? report[10] : 0) : report[8]),
                Y = (sbyte)(isOfficialBleReport ? (report.Length > 11 ? report[11] : 0) : report[9])
            };

            PenButtons =
            [
                report[1].IsBitSet(1),
                report[1].IsBitSet(2)
            ];
            Eraser = !isOfficialBleReport && report[1].IsBitSet(2);
        }

        public byte[] Raw { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Tilt { get; set; }
        public uint Pressure { get; set; }
        public bool[] PenButtons { get; set; }
        public bool Eraser { get; set; }
    }

}
