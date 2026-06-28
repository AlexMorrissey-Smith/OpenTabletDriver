using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Devices;

namespace OpenTabletDriver.Devices.MacOSHid
{
    [DeviceHub]
    public sealed class MacOSWH851HidRootHub : IDeviceHub
    {
        private const int VendorId = 0x256c;
        private const int UsbProductId = 0x2003;
        private const int BluetoothProductId = 0x8251;
        private const int DigitizerUsagePage = 0x000d;
        private const int DigitizerUsage = 0x0002;
        private const int MaxX = 40640;
        private const int MaxY = 25400;
        private const int MaxPressure = 16383;
        private const long OpenTabletDriverSyntheticDeviceId = 5303613955435230461;
        private static int accessStateLogged;
        private static readonly ConcurrentDictionary<string, byte> loggedDevices = new();

        private readonly Timer? timer;
        private List<IDeviceEndpoint> endpoints = [];

        public MacOSWH851HidRootHub()
        {
            if (SystemInterop.CurrentPlatform != PluginPlatform.MacOS)
                return;

            endpoints = Enumerate().Cast<IDeviceEndpoint>().ToList();
            timer = new Timer(PollDevices, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public event EventHandler<DevicesChangedEventArgs>? DevicesChanged;

        public IEnumerable<IDeviceEndpoint> GetDevices() =>
            SystemInterop.CurrentPlatform == PluginPlatform.MacOS ? Enumerate() : [];

        private void PollDevices(object? state)
        {
            var current = Enumerate().Cast<IDeviceEndpoint>().ToList();
            var changes = new DevicesChangedEventArgs(endpoints, current);
            if (changes.Changes.Any())
            {
                endpoints = current;
                DevicesChanged?.Invoke(this, changes);
            }
        }

        private static IEnumerable<MacOSWH851HidEndpoint> Enumerate()
        {
            var manager = IOHID.IOHIDManagerCreate(IntPtr.Zero, 0);
            if (manager == IntPtr.Zero)
                yield break;

            IOHID.IOHIDManagerSetDeviceMatching(manager, IntPtr.Zero);
            IOHID.IOHIDManagerOpen(manager, IOHID.IOHIDOptionsTypeNone);

            var devices = IOHID.IOHIDManagerCopyDevices(manager);
            if (devices == IntPtr.Zero)
            {
                IOHID.CFRelease(manager);
                yield break;
            }

            try
            {
                var count = IOHID.CFSetGetCount(devices);
                var values = new IntPtr[count];
                IOHID.CFSetGetValues(devices, values);

                foreach (var device in values)
                {
                    var vendorId = IOHID.GetIntProperty(device, "VendorID", -1);
                    var productId = IOHID.GetIntProperty(device, "ProductID", -1);
                    var usagePage = IOHID.GetIntProperty(device, "PrimaryUsagePage", -1);
                    var usage = IOHID.GetIntProperty(device, "PrimaryUsage", -1);

                    if (vendorId == VendorId && IsWH851Product(productId) && usagePage == DigitizerUsagePage && usage == DigitizerUsage)
                    {
                        LogMatchedDevice(device, productId);
                        yield return new MacOSWH851HidEndpoint(IOHID.CFRetain(device));
                    }
                }
            }
            finally
            {
                IOHID.CFRelease(devices);
                IOHID.CFRelease(manager);
            }
        }

        private static bool IsWH851Product(int productId) =>
            productId == UsbProductId || productId == BluetoothProductId;

        internal static byte[] CreateSyntheticBluetoothPenReport(long tabletX, long tabletY, double pressure, long buttons, double tiltX, double tiltY)
        {
            var x = ScaleTabletAxis(tabletX, MaxX);
            var y = ScaleTabletAxis(tabletY, MaxY);
            var pressureValue = (ushort)Math.Clamp((int)Math.Round(pressure * MaxPressure), 0, MaxPressure);
            var tiltXValue = (sbyte)Math.Clamp((int)Math.Round(tiltX * 127), -128, 127);
            var tiltYValue = (sbyte)Math.Clamp((int)Math.Round(tiltY * 127), -128, 127);

            var report = new byte[10];
            report[0] = 0x0a;
            report[1] = 0x40;
            if (pressureValue > 0 || (buttons & 0x01) != 0)
                report[1] |= 0x01;
            if ((buttons & 0x02) != 0)
                report[1] |= 0x02;
            if ((buttons & 0x04) != 0)
                report[1] |= 0x04;
            WriteUshort(report, 2, x);
            WriteUshort(report, 4, y);
            WriteUshort(report, 6, pressureValue);
            report[8] = unchecked((byte)tiltXValue);
            report[9] = unchecked((byte)tiltYValue);

            return report;
        }

        private static ushort ScaleTabletAxis(long value, int max)
        {
            if (value <= 0)
                return 0;

            if (value <= max)
                return (ushort)value;

            // macOS may expose normalized 16-bit tablet coordinates instead of device coordinates.
            return (ushort)Math.Clamp((int)Math.Round(value / (double)ushort.MaxValue * max), 0, max);
        }

        private static void WriteUshort(byte[] report, int offset, ushort value)
        {
            report[offset] = (byte)(value & 0xff);
            report[offset + 1] = (byte)(value >> 8);
        }

        private static void LogMatchedDevice(IntPtr device, int productId)
        {
            var locationId = IOHID.GetIntProperty(device, "LocationID");
            var reportLength = IOHID.GetIntProperty(device, "MaxInputReportSize", 0);
            var transport = IOHID.GetStringProperty(device, "Transport") ?? "Unknown";
            var key = $"{productId:x4}:{locationId:x8}:{reportLength}:{transport}";

            if (loggedDevices.TryAdd(key, 0))
            {
                Log.Debug(
                    "WH851 macOS HID",
                    $"Matched {transport} digitizer endpoint product=0x{productId:X4}, location=0x{locationId:X8}, inputReportLength={reportLength}."
                );
            }
        }

        private static bool EnsureListenAccess()
        {
            var access = IOHID.IOHIDCheckAccess(IOHID.IOHIDRequestTypeListenEvent);
            if (access == IOHID.IOHIDAccessTypeGranted)
            {
                if (Interlocked.Exchange(ref accessStateLogged, 1) == 0)
                    Log.Debug("WH851 macOS HID", "Input Monitoring access is granted.");
                return true;
            }

            var requested = IOHID.IOHIDRequestAccess(IOHID.IOHIDRequestTypeListenEvent);
            access = IOHID.IOHIDCheckAccess(IOHID.IOHIDRequestTypeListenEvent);
            if (access == IOHID.IOHIDAccessTypeGranted)
            {
                Log.Debug("WH851 macOS HID", "Input Monitoring access was granted after request.");
                return true;
            }

            if (Interlocked.Exchange(ref accessStateLogged, 1) == 0)
            {
                var state = access switch
                {
                    IOHID.IOHIDAccessTypeDenied => "denied",
                    IOHID.IOHIDAccessTypeUnknown => "unknown",
                    _ => access.ToString()
                };
                Log.Write(
                    "WH851 macOS HID",
                    $"Input Monitoring access is {state}; IOHIDRequestAccess returned {requested}. Enable Input Monitoring for OpenTabletDriver and its daemon.",
                    LogLevel.Warning
                );
            }

            return false;
        }

        private sealed class MacOSWH851HidEndpoint : IDeviceEndpoint
        {
            private readonly IntPtr device;
            private readonly bool requiresListenAccess;
            private readonly string? bluetoothPeripheralIdentifier;
            private readonly int locationId;
            private readonly int usagePage;
            private readonly int usage;

            public MacOSWH851HidEndpoint(IntPtr device)
            {
                this.device = device;
                VendorID = IOHID.GetIntProperty(device, "VendorID");
                ProductID = IOHID.GetIntProperty(device, "ProductID");
                requiresListenAccess = ProductID == BluetoothProductId;
                locationId = IOHID.GetIntProperty(device, "LocationID");
                usagePage = IOHID.GetIntProperty(device, "PrimaryUsagePage");
                usage = IOHID.GetIntProperty(device, "PrimaryUsage");
                InputReportLength = IOHID.GetIntProperty(device, "MaxInputReportSize", 10);
                OutputReportLength = IOHID.GetIntProperty(device, "MaxOutputReportSize", 1);
                FeatureReportLength = IOHID.GetIntProperty(device, "MaxFeatureReportSize", 1);
                Manufacturer = IOHID.GetStringProperty(device, "Manufacturer") ?? "HUION";
                ProductName = IOHID.GetStringProperty(device, "Product") ?? "WH851";
                FriendlyName = ProductName;
                SerialNumber = IOHID.GetStringProperty(device, "SerialNumber") ?? string.Empty;
                bluetoothPeripheralIdentifier = IOHID.GetStringProperty(device, "PhysicalDeviceUniqueID");
                DeviceAttributes = new Dictionary<string, string>
                {
                    ["Transport"] = IOHID.GetStringProperty(device, "Transport") ?? "Bluetooth Low Energy",
                    ["MacOSHIDUsagePage"] = usagePage.ToString(),
                    ["MacOSHIDUsage"] = usage.ToString()
                };
            }

            ~MacOSWH851HidEndpoint()
            {
                IOHID.CFRelease(device);
            }

            public int ProductID { get; }
            public int VendorID { get; }
            public int InputReportLength { get; }
            public int OutputReportLength { get; }
            public int FeatureReportLength { get; }
            public string? Manufacturer { get; }
            public string? ProductName { get; }
            public string? FriendlyName { get; }
            public string? SerialNumber { get; }
            public string DevicePath => $"iohid://wh851/{locationId:x8}/{usagePage:x4}/{usage:x4}";
            public bool CanOpen => true;
            public IDictionary<string, string> DeviceAttributes { get; }

            public IDeviceEndpointStream Open() => new MacOSWH851HidEndpointStream(device, InputReportLength, requiresListenAccess, bluetoothPeripheralIdentifier);

            public string? GetDeviceString(byte index) => null;
        }

        private sealed class MacOSWH851HidEndpointStream : IDeviceEndpointStream
        {
            private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(2);
            private readonly IntPtr device;
            private readonly int reportLength;
            private readonly bool requiresListenAccess;
            private readonly string? bluetoothPeripheralIdentifier;
            private readonly BlockingCollection<byte[]> reports = new();
            private readonly ManualResetEventSlim ready = new();
            private readonly Thread runLoopThread;
            private readonly IOHID.IOHIDReportCallback reportCallback;
            private readonly CoreGraphics.CGEventTapCallBack eventTapCallback;
            private Process? bluetoothBridgeProcess;
            private Thread? bluetoothBridgeReaderThread;
            private Thread? bluetoothBridgeErrorThread;
            private IntPtr reportBuffer;
            private IntPtr runLoop;
            private IntPtr manager;
            private IntPtr eventTap;
            private IntPtr eventTapSource;
            private GCHandle gcHandle;
            private volatile bool disposed;
            private int openResult = -1;
            private int usingCoreBluetoothBridge;
            private int loggedReports;
            private int loggedEvents;
            private int loggedRawEvents;
            private int loggedNativeSuppressed;
            private int loggedSyntheticPassed;
            private int loggedTapDisabled;
            private int loggedSyntheticReports;

            public MacOSWH851HidEndpointStream(IntPtr device, int reportLength, bool requiresListenAccess, string? bluetoothPeripheralIdentifier)
            {
                this.device = IOHID.CFRetain(device);
                this.reportLength = Math.Max(reportLength, 1);
                this.requiresListenAccess = requiresListenAccess;
                this.bluetoothPeripheralIdentifier = bluetoothPeripheralIdentifier;
                reportCallback = OnReport;
                eventTapCallback = OnEventTap;
                runLoopThread = new Thread(Run)
                {
                    Name = "WH851 macOS HID report loop",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };

                runLoopThread.Start();
                if (!ready.Wait(OpenTimeout))
                {
                    disposed = true;
                    reports.CompleteAdding();
                    Log.Write("WH851 macOS HID", "Timed out opening WH851 macOS HID endpoint.", LogLevel.Warning);
                    throw new IOException("Timed out opening WH851 macOS HID endpoint.");
                }

                if (openResult != 0)
                {
                    Dispose();
                    throw new IOException($"Unable to open WH851 macOS HID endpoint. IOHIDDeviceOpen returned 0x{openResult:X}.");
                }
            }

            public byte[] Read()
            {
                try
                {
                    return reports.Take();
                }
                catch (InvalidOperationException ex)
                {
                    throw new IOException("I/O disconnected.", ex);
                }
            }

            public void Write(byte[] buffer)
            {
                if (buffer.Length == 0)
                    return;

                var reportId = buffer[0];
                var result = IOHID.IOHIDDeviceSetReport(device, IOHID.IOHIDReportTypeOutput, reportId, buffer, buffer.Length);
                if (result != 0)
                    throw new IOException($"Unable to write WH851 macOS HID output report. IOHIDDeviceSetReport returned 0x{result:X}.");
            }

            public void GetFeature(byte[] buffer)
            {
                if (buffer.Length == 0)
                    return;

                var length = (nint)buffer.Length;
                var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    var result = IOHID.IOHIDDeviceGetReport(device, IOHID.IOHIDReportTypeFeature, buffer[0], handle.AddrOfPinnedObject(), ref length);
                    if (result != 0)
                        throw new IOException($"Unable to read WH851 macOS HID feature report. IOHIDDeviceGetReport returned 0x{result:X}.");
                }
                finally
                {
                    handle.Free();
                }
            }

            public void SetFeature(byte[] buffer)
            {
                if (buffer.Length == 0)
                    return;

                var result = IOHID.IOHIDDeviceSetReport(device, IOHID.IOHIDReportTypeFeature, buffer[0], buffer, buffer.Length);
                if (result != 0)
                    throw new IOException($"Unable to write WH851 macOS HID feature report. IOHIDDeviceSetReport returned 0x{result:X}.");
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                ready.Set();
                reports.CompleteAdding();
                StopBluetoothBridge();

                if (runLoop != IntPtr.Zero)
                {
                    IOHID.CFRunLoopStop(runLoop);
                    IOHID.CFRunLoopWakeUp(runLoop);
                }

                var stopped = !runLoopThread.IsAlive || runLoopThread.Join(TimeSpan.FromSeconds(2));
                if (!stopped)
                {
                    Log.Write("WH851 macOS HID", "Timed out waiting for IOHID report loop to stop.", LogLevel.Warning);
                    return;
                }

                if (gcHandle.IsAllocated)
                    gcHandle.Free();

                ready.Dispose();
                reports.Dispose();

                if (reportBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(reportBuffer);
                    reportBuffer = IntPtr.Zero;
                }

                IOHID.CFRelease(device);
                GC.SuppressFinalize(this);
            }

            ~MacOSWH851HidEndpointStream()
            {
                Dispose();
            }

            private void Run()
            {
                try
                {
                    runLoop = IOHID.CFRunLoopGetCurrent();
                    if (requiresListenAccess && !EnsureListenAccess())
                    {
                        openResult = -1;
                        ready.Set();
                        return;
                    }

                    gcHandle = GCHandle.Alloc(this);

                    if (requiresListenAccess)
                    {
                        openResult = OpenBluetoothBridge();
                        if (openResult == 0)
                        {
                            Interlocked.Exchange(ref usingCoreBluetoothBridge, 1);
                            if (OpenBluetoothEventTap() != 0)
                                Log.Write("WH851 macOS HID", "CoreBluetooth bridge is active, but Quartz suppression for native Bluetooth tablet events could not be installed.", LogLevel.Warning);
                        }
                        else
                            openResult = OpenBluetoothDevice();
                        if (openResult != 0)
                            openResult = OpenBluetoothEventTap();
                    }
                    else
                        openResult = OpenInputReportDevice(IOHID.IOHIDOptionsTypeNone, "USB endpoint");

                    ready.Set();

                    while (!disposed)
                        IOHID.CFRunLoopRunInMode(IOHID.CFRunLoopDefaultMode, 0.25, false);
                }
                finally
                {
                    if (openResult == 0)
                    {
                        if (manager != IntPtr.Zero)
                        {
                            IOHID.IOHIDManagerUnscheduleFromRunLoop(manager, runLoop, IOHID.CFRunLoopDefaultMode);
                            IOHID.IOHIDManagerClose(manager, IOHID.IOHIDOptionsTypeNone);
                            IOHID.CFRelease(manager);
                            manager = IntPtr.Zero;
                        }
                        if (eventTap != IntPtr.Zero)
                        {
                            if (eventTapSource != IntPtr.Zero)
                            {
                                IOHID.CFRunLoopRemoveSource(runLoop, eventTapSource, IOHID.CFRunLoopDefaultMode);
                                IOHID.CFRelease(eventTapSource);
                                eventTapSource = IntPtr.Zero;
                            }

                            CoreGraphics.CGEventTapEnable(eventTap, false);
                            IOHID.CFRelease(eventTap);
                            eventTap = IntPtr.Zero;
                        }

                        if (reportBuffer != IntPtr.Zero)
                        {
                            IOHID.IOHIDDeviceUnscheduleFromRunLoop(device, runLoop, IOHID.CFRunLoopDefaultMode);
                            IOHID.IOHIDDeviceClose(device, IOHID.IOHIDOptionsTypeNone);
                        }
                    }

                    reports.CompleteAdding();
                    ready.Set();
                }
            }

            private int OpenBluetoothBridge()
            {
                if (string.IsNullOrWhiteSpace(bluetoothPeripheralIdentifier))
                {
                    Log.Write("WH851 macOS HID", "Bluetooth peripheral identifier is unavailable; cannot use CoreBluetooth bridge.", LogLevel.Warning);
                    return -1;
                }

                var bridgePath = Path.Combine(AppContext.BaseDirectory, "OpenTabletDriver.WH851BleBridge");
                if (!File.Exists(bridgePath))
                {
                    Log.Write("WH851 macOS HID", $"CoreBluetooth bridge helper was not found at '{bridgePath}'.", LogLevel.Warning);
                    return -1;
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = bridgePath,
                        ArgumentList = { bluetoothPeripheralIdentifier },
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                try
                {
                    if (!process.Start())
                        return -1;
                }
                catch (Exception ex)
                {
                    Log.Write("WH851 macOS HID", $"Unable to start CoreBluetooth bridge helper: {ex.Message}", LogLevel.Warning);
                    process.Dispose();
                    return -1;
                }

                bluetoothBridgeProcess = process;
                bluetoothBridgeReaderThread = new Thread(ReadBluetoothBridgeReports)
                {
                    Name = "WH851 CoreBluetooth report reader",
                    IsBackground = true
                };
                bluetoothBridgeErrorThread = new Thread(ReadBluetoothBridgeErrors)
                {
                    Name = "WH851 CoreBluetooth diagnostics reader",
                    IsBackground = true
                };
                bluetoothBridgeReaderThread.Start(process);
                bluetoothBridgeErrorThread.Start(process);

                Log.Debug("WH851 macOS HID", $"Using CoreBluetooth bridge path for Bluetooth endpoint {bluetoothPeripheralIdentifier}.");
                Log.Debug("WH851 macOS HID", "CoreBluetooth bridge active; native Bluetooth tablet events will be suppressed while OTD synthetic mouse events pass through.");
                return 0;
            }

            private void ReadBluetoothBridgeReports(object? state)
            {
                var process = (Process)state!;
                try
                {
                    while (!disposed && !process.StandardOutput.EndOfStream)
                    {
                        var line = process.StandardOutput.ReadLine();
                        if (line is null)
                            break;

                        if (!line.StartsWith("REPORT ", StringComparison.Ordinal))
                            continue;

                        var report = ParseHexReport(line[7..]);
                        if (report.Length == 0)
                            continue;

                        if (Interlocked.Increment(ref loggedReports) <= 8)
                            Log.Debug("WH851 Timing", $"{Stopwatch.GetTimestamp()} BLE bridge read report={BitConverter.ToString(report)}");

                        EnqueueReport(report);
                    }
                }
                catch (Exception ex) when (!disposed)
                {
                    Log.Write("WH851 macOS HID", $"CoreBluetooth bridge report reader failed: {ex.Message}", LogLevel.Warning);
                }
            }

            private static byte[] ParseHexReport(string hex)
            {
                var parts = hex.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var report = new byte[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out report[i]))
                        return [];
                }

                return report;
            }

            private void ReadBluetoothBridgeErrors(object? state)
            {
                var process = (Process)state!;
                try
                {
                    while (!disposed && !process.StandardError.EndOfStream)
                    {
                        var line = process.StandardError.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                            Log.Debug("WH851 macOS HID", line);
                    }
                }
                catch (Exception ex) when (!disposed)
                {
                    Log.Write("WH851 macOS HID", $"CoreBluetooth bridge diagnostics reader failed: {ex.Message}", LogLevel.Warning);
                }
            }

            private void StopBluetoothBridge()
            {
                var process = bluetoothBridgeProcess;
                bluetoothBridgeProcess = null;
                if (process is null)
                    return;

                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            private int OpenInputReportDevice(int options, string endpointDescription)
            {
                var result = IOHID.IOHIDDeviceOpen(device, options);
                if (result != 0)
                {
                    Log.Write("WH851 macOS HID", $"IOHIDDeviceOpen returned 0x{result:X} for {endpointDescription}.", LogLevel.Warning);
                    return result;
                }

                reportBuffer = Marshal.AllocHGlobal(reportLength);
                IOHID.IOHIDDeviceRegisterInputReportCallback(device, reportBuffer, reportLength, reportCallback, GCHandle.ToIntPtr(gcHandle));
                IOHID.IOHIDDeviceScheduleWithRunLoop(device, runLoop, IOHID.CFRunLoopDefaultMode);
                Log.Debug("WH851 macOS HID", $"Using IOHID device input report callback path for {endpointDescription}.");
                return 0;
            }

            private int OpenBluetoothDevice()
            {
                var result = OpenInputReportDevice(IOHID.IOHIDOptionsTypeSeizeDevice, "Bluetooth endpoint with seize");
                if (result == 0)
                    return 0;

                result = OpenInputReportDevice(IOHID.IOHIDOptionsTypeNone, "Bluetooth endpoint without seize");
                if (result == 0)
                    return 0;

                Log.Write("WH851 macOS HID", "Falling back to Quartz event tap path for Bluetooth tablet events.", LogLevel.Warning);
                return result;
            }

            private int OpenBluetoothEventTap()
            {
                // Native WH851 events can arrive as tablet-subtyped mouse events. Keep them in the
                // mask so they can be suppressed, but allow OTD's marked synthetic events below.
                var mask = CoreGraphics.Mask(
                    CoreGraphics.kCGEventMouseMoved,
                    CoreGraphics.kCGEventLeftMouseDragged,
                    CoreGraphics.kCGEventRightMouseDragged,
                    CoreGraphics.kCGEventOtherMouseDragged,
                    CoreGraphics.kCGEventLeftMouseDown,
                    CoreGraphics.kCGEventLeftMouseUp,
                    CoreGraphics.kCGEventRightMouseDown,
                    CoreGraphics.kCGEventRightMouseUp,
                    CoreGraphics.kCGEventOtherMouseDown,
                    CoreGraphics.kCGEventOtherMouseUp,
                    CoreGraphics.kCGEventTabletPointer,
                    CoreGraphics.kCGEventTabletProximity
                );

                eventTap = CoreGraphics.CGEventTapCreate(
                    CoreGraphics.kCGHIDEventTap,
                    CoreGraphics.kCGHeadInsertEventTap,
                    CoreGraphics.kCGEventTapOptionDefault,
                    mask,
                    eventTapCallback,
                    GCHandle.ToIntPtr(gcHandle)
                );

                if (eventTap == IntPtr.Zero)
                {
                    eventTap = CoreGraphics.CGEventTapCreate(
                        CoreGraphics.kCGSessionEventTap,
                        CoreGraphics.kCGHeadInsertEventTap,
                        CoreGraphics.kCGEventTapOptionDefault,
                        mask,
                        eventTapCallback,
                        GCHandle.ToIntPtr(gcHandle)
                    );
                }

                if (eventTap == IntPtr.Zero)
                {
                    Log.Write("WH851 macOS HID", "Unable to create Quartz event tap for Bluetooth endpoint.", LogLevel.Warning);
                    return -1;
                }

                eventTapSource = IOHID.CFMachPortCreateRunLoopSource(IntPtr.Zero, eventTap, 0);
                if (eventTapSource == IntPtr.Zero)
                {
                    Log.Write("WH851 macOS HID", "Unable to create run loop source for Quartz event tap.", LogLevel.Warning);
                    return -1;
                }

                IOHID.CFRunLoopAddSource(runLoop, eventTapSource, IOHID.CFRunLoopDefaultMode);
                CoreGraphics.CGEventTapEnable(eventTap, true);
                Log.Debug("WH851 macOS HID", "Using Quartz event tap path for Bluetooth tablet events.");
                return 0;
            }

            private int OpenBluetoothManager()
            {
                manager = IOHID.IOHIDManagerCreate(IntPtr.Zero, 0);
                if (manager == IntPtr.Zero)
                    return -1;

                var matching = IOHID.CreateIntDictionary(
                    ("VendorID", VendorId),
                    ("ProductID", BluetoothProductId),
                    ("PrimaryUsagePage", DigitizerUsagePage),
                    ("PrimaryUsage", DigitizerUsage)
                );

                IOHID.IOHIDManagerSetDeviceMatching(manager, matching);
                IOHID.CFRelease(matching);
                IOHID.IOHIDManagerRegisterInputReportCallback(manager, reportCallback, GCHandle.ToIntPtr(gcHandle));
                IOHID.IOHIDManagerScheduleWithRunLoop(manager, runLoop, IOHID.CFRunLoopDefaultMode);

                Log.Debug("WH851 macOS HID", "Using IOHID manager input report callback path for Bluetooth endpoint.");
                var result = IOHID.IOHIDManagerOpen(manager, IOHID.IOHIDOptionsTypeNone);
                if (result != 0)
                    Log.Write("WH851 macOS HID", $"IOHIDManagerOpen returned 0x{result:X}; keeping Bluetooth listener scheduled.", LogLevel.Warning);

                return 0;
            }

            private static IntPtr OnEventTap(IntPtr proxy, uint type, IntPtr eventRef, IntPtr userInfo)
            {
                if (userInfo == IntPtr.Zero)
                    return eventRef;

                var stream = (MacOSWH851HidEndpointStream)GCHandle.FromIntPtr(userInfo).Target!;
                if (stream.disposed)
                    return eventRef;

                if (type is CoreGraphics.kCGEventTapDisabledByTimeout or CoreGraphics.kCGEventTapDisabledByUserInput)
                {
                    if (Interlocked.Exchange(ref stream.loggedTapDisabled, 1) == 0)
                        Log.Write("WH851 macOS HID", $"Quartz event tap was disabled by macOS event type={type}; re-enabling.", LogLevel.Warning);

                    if (stream.eventTap != IntPtr.Zero)
                        CoreGraphics.CGEventTapEnable(stream.eventTap, true);
                    return eventRef;
                }

                var subtype = CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGMouseEventSubtype);
                var location = CoreGraphics.CGEventGetLocation(eventRef);
                var x = CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGTabletEventPointX);
                var y = CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGTabletEventPointY);
                var buttons = CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGTabletEventPointButtons);
                var pressure = CoreGraphics.CGEventGetDoubleValueField(eventRef, CoreGraphics.kCGTabletEventPointPressure);
                var tiltX = CoreGraphics.CGEventGetDoubleValueField(eventRef, CoreGraphics.kCGTabletEventTiltX);
                var tiltY = CoreGraphics.CGEventGetDoubleValueField(eventRef, CoreGraphics.kCGTabletEventTiltY);
                var deviceId = CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGTabletEventDeviceID);
                var eventSourceUserData = GetEventSourceUserData(eventRef);
                var hasUsableTabletPointFields = x != 0 || y != 0 || buttons != 0 || pressure != 0 || tiltX != 0 || tiltY != 0;
                var tabletPoint = type == CoreGraphics.kCGEventTabletPointer
                    || subtype == CoreGraphics.kCGEventMouseSubtypeTabletPoint;
                var tabletProximity = type == CoreGraphics.kCGEventTabletProximity || subtype == CoreGraphics.kCGEventMouseSubtypeTabletProximity;

                if (Interlocked.Increment(ref stream.loggedRawEvents) <= 24)
                {
                    Log.Debug(
                        "WH851 macOS HID",
                        $"Quartz raw event type={type} subtype={subtype} loc=<{location.X:0.##}, {location.Y:0.##}> tablet=<{x}, {y}> pressure={pressure:0.###} buttons=0x{buttons:X} tilt=<{tiltX:0.###}, {tiltY:0.###}> device={deviceId} classifiedPoint={tabletPoint} classifiedProximity={tabletProximity}."
                    );
                }

                if (!tabletPoint && !tabletProximity)
                    return eventRef;

                if (IsOpenTabletDriverSyntheticEvent(deviceId, eventSourceUserData))
                {
                    if (Interlocked.Exchange(ref stream.loggedSyntheticPassed, 1) == 0)
                        Log.Debug("WH851 macOS HID", $"Passing OTD synthetic Quartz event type={type} subtype={subtype} source={eventSourceUserData} device={deviceId}.");
                    return eventRef;
                }

                if (Volatile.Read(ref stream.usingCoreBluetoothBridge) != 0)
                {
                    if (Interlocked.Exchange(ref stream.loggedNativeSuppressed, 1) == 0)
                        Log.Debug("WH851 macOS HID", "Suppressing native Quartz tablet events while CoreBluetooth bridge is active.");
                    return IntPtr.Zero;
                }

                if (tabletPoint && !hasUsableTabletPointFields)
                    return IntPtr.Zero;

                if (Interlocked.Increment(ref stream.loggedEvents) <= 12)
                {
                    Log.Debug("WH851 macOS HID", $"Quartz event type={type} subtype={subtype} loc=<{location.X:0.##}, {location.Y:0.##}> tablet=<{x}, {y}> pressure={pressure:0.###}.");
                }

                if (tabletProximity && CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGTabletProximityEventEnterProximity) == 0)
                {
                    stream.EnqueueReport([0x0a, 0x00, 0, 0, 0, 0, 0, 0, 0, 0]);
                    return IntPtr.Zero;
                }

                if (tabletPoint)
                    stream.EnqueueTabletPoint(eventRef);

                return IntPtr.Zero;
            }

            private static bool IsOpenTabletDriverSyntheticEvent(long deviceId, long eventSourceUserData) =>
                deviceId == OpenTabletDriverSyntheticDeviceId || eventSourceUserData == OpenTabletDriverSyntheticDeviceId;

            private static long GetEventSourceUserData(IntPtr eventRef)
            {
                var source = CoreGraphics.CGEventCreateSourceFromEvent(eventRef);
                if (source == IntPtr.Zero)
                    return 0;

                try
                {
                    return CoreGraphics.CGEventSourceGetUserData(source);
                }
                finally
                {
                    IOHID.CFRelease(source);
                }
            }

            private void EnqueueTabletPoint(IntPtr eventRef)
            {
                var report = CreateSyntheticBluetoothPenReport(
                    CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGTabletEventPointX),
                    CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGTabletEventPointY),
                    CoreGraphics.CGEventGetDoubleValueField(eventRef, CoreGraphics.kCGTabletEventPointPressure),
                    CoreGraphics.CGEventGetIntegerValueField(eventRef, CoreGraphics.kCGTabletEventPointButtons),
                    CoreGraphics.CGEventGetDoubleValueField(eventRef, CoreGraphics.kCGTabletEventTiltX),
                    CoreGraphics.CGEventGetDoubleValueField(eventRef, CoreGraphics.kCGTabletEventTiltY)
                );

                if (Interlocked.Increment(ref loggedSyntheticReports) <= 12)
                    Log.Debug("WH851 macOS HID", $"Synthetic report: {BitConverter.ToString(report)}");

                EnqueueReport(report);
            }

            private void EnqueueReport(byte[] report)
            {
                if (!reports.IsAddingCompleted)
                    reports.Add(report);
            }

            private static void OnReport(IntPtr context, int result, IntPtr sender, int type, uint reportId, IntPtr report, nint reportLength)
            {
                if (context == IntPtr.Zero)
                    return;

                var stream = (MacOSWH851HidEndpointStream)GCHandle.FromIntPtr(context).Target!;
                if (stream.disposed || result != 0 || type != IOHID.IOHIDReportTypeInput)
                    return;

                var length = (int)reportLength;
                if (length <= 0)
                    return;

                var data = new byte[length];
                Marshal.Copy(report, data, 0, length);

                if (reportId != 0 && data[0] != reportId)
                    data = [(byte)reportId, .. data];

                if (Interlocked.Increment(ref stream.loggedReports) <= 8)
                    Log.Debug("WH851 macOS HID", $"Input report: {BitConverter.ToString(data)}");

                if (!stream.reports.IsAddingCompleted)
                    stream.reports.Add(data);
            }
        }
    }
}
