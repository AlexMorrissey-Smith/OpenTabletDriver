using System;
using System.Collections.Generic;
using System.IO;
using OpenTabletDriver.Plugin.Devices;

namespace OpenTabletDriver.Devices.WindowsBluetoothBackend
{
    internal sealed class WH851BluetoothGattEndpoint : IDeviceEndpoint
    {
        public WH851BluetoothGattEndpoint(string devicePath, Guid serviceUuid, string? bluetoothAddress)
        {
            DevicePath = devicePath;
            _serviceUuid = serviceUuid;
            SerialNumber = bluetoothAddress;
        }

        private readonly Guid _serviceUuid;

        public int ProductID => WH851WindowsBluetoothGattRootHub.ProductId;
        public int VendorID => WH851WindowsBluetoothGattRootHub.VendorId;
        public int InputReportLength => 12;
        public int OutputReportLength => 0;
        public int FeatureReportLength => 0;
        public string Manufacturer => "GAOMON";
        public string ProductName => "WH851 Bluetooth";
        public string FriendlyName => "Gaomon WH851 Bluetooth";
        public string? SerialNumber { get; }
        public string DevicePath { get; }
        public bool CanOpen => WH851BluetoothGattEndpointStream.CanOpenConnected(DevicePath);
        public IDictionary<string, string> DeviceAttributes => new Dictionary<string, string>
        {
            ["BluetoothServiceUuid"] = _serviceUuid.ToString()
        };

        public IDeviceEndpointStream Open()
        {
            return WH851BluetoothGattEndpointStream.Open(DevicePath)
                ?? throw new IOException($"Failed to open WH851 Bluetooth GATT service '{DevicePath}'.");
        }

        public string? GetDeviceString(byte index)
        {
            return null;
        }
    }
}
