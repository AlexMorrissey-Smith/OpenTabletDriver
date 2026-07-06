using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace OpenTabletDriver.Tests
{
    // Ported from upstream PR #4864: regression tests for the Windows BLE GATT
    // backend's native struct layout assumptions and hardware-id matching.
    // The transport-priority tests were not ported — 0.6.x MatchDevice already
    // dedupes transports via identifier order (first match wins).
    public sealed class WH851WindowsBluetoothTests
    {
        [Fact]
        public void BluetoothLeHidChildMatcherAcceptsWindowsBthLeDeviceHardwareId()
        {
            Assert.True(IsBluetoothLeHidChild(
                new[]
                {
                    @"BTHLEDevice\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02256c_PID&8251_REV&0001",
                    @"BTHLEDevice\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02256c_PID&8251",
                    @"BTHLEDevice\{00001812-0000-1000-8000-00805f9b34fb}_LOCALMFG&0002"
                },
                vendorId: 0x256c,
                productId: 0x8251));
        }

        [Fact]
        public void BluetoothLeHidChildMatcherAcceptsWindowsHidCollectionHardwareId()
        {
            Assert.True(IsBluetoothLeHidChild(
                new[]
                {
                    @"HID\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02256c_PID&8251_REV&0001&Col01",
                    @"HID\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02256c_PID&8251&Col01"
                },
                vendorId: 0x256c,
                productId: 0x8251));
        }

        [Fact]
        public void BluetoothLeHidChildMatcherRejectsDifferentDevice()
        {
            Assert.False(IsBluetoothLeHidChild(
                new[]
                {
                    @"BTHLEDevice\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02046d_PID&b386_REV&0008"
                },
                vendorId: 0x256c,
                productId: 0x8251));
        }

        [Fact]
        public void BluetoothGattClientCharacteristicConfigurationValueMatchesWindowsSdkLayout()
        {
            var descriptorValueType = typeof(Driver).Assembly.GetType(
                "OpenTabletDriver.Devices.WindowsBluetoothBackend.WindowsBluetoothGattNative+BTH_LE_GATT_DESCRIPTOR_VALUE"
            );
            Assert.NotNull(descriptorValueType);

            Assert.Equal(80, Marshal.SizeOf(descriptorValueType!));
            Assert.Equal(0, Marshal.OffsetOf(descriptorValueType!, "DescriptorType").ToInt32());
            Assert.Equal(4, Marshal.OffsetOf(descriptorValueType!, "DescriptorUuid").ToInt32());
            Assert.Equal(24, Marshal.OffsetOf(descriptorValueType!, "IsSubscribeToNotification").ToInt32());
            Assert.Equal(25, Marshal.OffsetOf(descriptorValueType!, "IsSubscribeToIndication").ToInt32());
            Assert.Equal(72, Marshal.OffsetOf(descriptorValueType!, "DataSize").ToInt32());
        }

        [Fact]
        public void BluetoothGattCharacteristicValueDataOffsetMatchesWindowsSdkLayout()
        {
            var nativeType = typeof(Driver).Assembly.GetType(
                "OpenTabletDriver.Devices.WindowsBluetoothBackend.WindowsBluetoothGattNative"
            );
            Assert.NotNull(nativeType);

            var dataOffset = nativeType!.GetField(
                "BTH_LE_GATT_CHARACTERISTIC_VALUE_DATA_OFFSET",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.NotNull(dataOffset);

            Assert.Equal(4, dataOffset!.GetRawConstantValue());
        }

        private static bool IsBluetoothLeHidChild(IReadOnlyList<string> hardwareIds, int vendorId, int productId)
        {
            var type = typeof(Driver).Assembly.GetType("OpenTabletDriver.Devices.WindowsBluetoothBackend.WindowsBluetoothGattDeviceInterfaceEnumerator");
            Assert.NotNull(type);

            var method = type!.GetMethod("IsBluetoothLeHidChild", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            return (bool)method!.Invoke(null, new object[] { hardwareIds, vendorId, productId })!;
        }
    }
}
