import CoreBluetooth
import CoreGraphics
import Foundation
import IOKit.hid

private let vendorId = 0x256c
private let usbProductId = 0x2003
private let bluetoothProductId = 0x8251
private let reportService = CBUUID(string: "FFE0")
private let reportCharacteristic = CBUUID(string: "FFE1")
private let commandCharacteristic = CBUUID(string: "FFE2")

private struct Step {
    let name: String
    let instruction: String
}

private let captureSteps: [Step] = [
    Step(name: "button 1", instruction: "press it"),
    Step(name: "button 2", instruction: "press it"),
    Step(name: "button 3", instruction: "press it"),
    Step(name: "button 4", instruction: "press it"),
    Step(name: "button 5", instruction: "press it"),
    Step(name: "button 6", instruction: "press it"),
    Step(name: "button 7", instruction: "press it"),
    Step(name: "button 8", instruction: "press it"),
    Step(name: "wheel button", instruction: "press it"),
    Step(name: "wheel CW", instruction: "rotate clockwise"),
    Step(name: "wheel CCW", instruction: "rotate counter-clockwise"),
    Step(name: "pen button 1", instruction: "click it"),
    Step(name: "pen button 2", instruction: "click it"),
    Step(name: "pen tap", instruction: "tap tablet")
]

private enum CapturePhase: String {
    case usb = "USB"
    case bluetooth = "Bluetooth"
}

private final class CaptureLogger {
    private let handle: FileHandle
    private let lock = NSLock()
    private let formatter = ISO8601DateFormatter()
    private var phase: CapturePhase?
    private var step: Step?
    private var stepIndex: Int?

    let logPath: String

    init(logPath: String) throws {
        self.logPath = logPath
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]

        let url = URL(fileURLWithPath: logPath)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        if !FileManager.default.fileExists(atPath: logPath) {
            FileManager.default.createFile(atPath: logPath, contents: nil)
        }

        handle = try FileHandle(forWritingTo: url)
        try handle.seekToEnd()
    }

    deinit {
        try? handle.close()
    }

    func setStep(phase: CapturePhase, step: Step, index: Int) {
        lock.lock()
        self.phase = phase
        self.step = step
        self.stepIndex = index
        lock.unlock()

        write(kind: "step_start", fields: [
            "phase": phase.rawValue,
            "step": step.name,
            "stepIndex": index,
            "instruction": step.instruction
        ])
    }

    func clearStep() {
        let oldPhase: CapturePhase?
        let oldStep: Step?
        let oldIndex: Int?

        lock.lock()
        oldPhase = phase
        oldStep = step
        oldIndex = stepIndex
        step = nil
        stepIndex = nil
        lock.unlock()

        var fields: [String: Any] = [:]
        if let oldPhase {
            fields["phase"] = oldPhase.rawValue
        }
        if let oldStep {
            fields["step"] = oldStep.name
        }
        if let oldIndex {
            fields["stepIndex"] = oldIndex
        }
        write(kind: "step_end", fields: fields)
    }

    func status(_ message: String, fields: [String: Any] = [:]) {
        var payload = fields
        payload["message"] = message
        write(kind: "status", fields: payload)
    }

    func event(source: String, fields: [String: Any]) {
        var payload = fields
        payload["source"] = source

        lock.lock()
        if let phase {
            payload["phase"] = phase.rawValue
        }
        if let step {
            payload["step"] = step.name
            payload["instruction"] = step.instruction
        }
        if let stepIndex {
            payload["stepIndex"] = stepIndex
        }
        lock.unlock()

        write(kind: "event", fields: payload)
    }

    private func write(kind: String, fields: [String: Any]) {
        var payload = fields
        payload["kind"] = kind
        payload["timestamp"] = formatter.string(from: Date())

        lock.lock()
        defer { lock.unlock() }

        do {
            let data = try JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys])
            handle.write(data)
            handle.write(Data([0x0a]))
        } catch {
            fputs("log write failed: \(error)\n", stderr)
        }
    }
}

private final class USBHIDCapture {
    private let logger: CaptureLogger
    private var thread: Thread?
    private var shouldStop = false
    private var manager: IOHIDManager?
    private var registrations: [USBDeviceRegistration] = []

    init(logger: CaptureLogger) {
        self.logger = logger
    }

    func start() {
        shouldStop = false
        thread = Thread { [weak self] in
            self?.run()
        }
        thread?.name = "WH851 USB HID capture"
        thread?.start()
    }

    func stop() {
        shouldStop = true
        Thread.sleep(forTimeInterval: 0.25)
    }

    private func run() {
        let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
        self.manager = manager

        let access = IOHIDCheckAccess(kIOHIDRequestTypeListenEvent)
        logger.status("USB listen access", fields: [
            "phase": CapturePhase.usb.rawValue,
            "source": "usb.iohid",
            "access": access.rawValue
        ])
        if access != kIOHIDAccessTypeGranted {
            let requested = IOHIDRequestAccess(kIOHIDRequestTypeListenEvent)
            logger.status("USB listen access requested", fields: [
                "phase": CapturePhase.usb.rawValue,
                "source": "usb.iohid",
                "requested": requested
            ])
        }

        let matching: [String: Any] = [
            kIOHIDVendorIDKey: vendorId,
            kIOHIDProductIDKey: usbProductId
        ]

        IOHIDManagerSetDeviceMatching(manager, matching as CFDictionary)
        IOHIDManagerScheduleWithRunLoop(manager, CFRunLoopGetCurrent(), CFRunLoopMode.defaultMode.rawValue)
        openDevices(manager: manager)

        while !shouldStop {
            CFRunLoopRunInMode(CFRunLoopMode.defaultMode, 0.1, false)
        }

        for registration in registrations {
            IOHIDDeviceUnscheduleFromRunLoop(registration.device, CFRunLoopGetCurrent(), CFRunLoopMode.defaultMode.rawValue)
            IOHIDDeviceClose(registration.device, IOOptionBits(kIOHIDOptionsTypeNone))
        }
        registrations.removeAll()
        IOHIDManagerUnscheduleFromRunLoop(manager, CFRunLoopGetCurrent(), CFRunLoopMode.defaultMode.rawValue)
    }

    private func openDevices(manager: IOHIDManager) {
        guard let devices = IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice> else {
            logger.status("USB WH851 not found by IOHID", fields: [
                "phase": CapturePhase.usb.rawValue,
                "source": "usb.iohid",
                "vendorId": vendorId,
                "productId": usbProductId
            ])
            return
        }

        logger.status("USB WH851 devices found", fields: [
            "phase": CapturePhase.usb.rawValue,
            "source": "usb.iohid",
            "count": devices.count
        ])

        for device in devices {
            let fields = deviceFields(device)
            logger.status("USB device", fields: fields)

            let reportLength = max(1, intProperty(device, kIOHIDMaxInputReportSizeKey))
            let result = IOHIDDeviceOpen(device, IOOptionBits(kIOHIDOptionsTypeNone))
            var openFields = fields
            openFields["ioreturn"] = result
            openFields["reportLength"] = reportLength
            logger.status("USB device open", fields: openFields)

            if result == kIOReturnSuccess {
                let registration = USBDeviceRegistration(device: device, reportLength: reportLength)
                registrations.append(registration)
                IOHIDDeviceRegisterInputReportCallback(
                    device,
                    registration.buffer,
                    registration.reportLength,
                    usbReportCallback,
                    Unmanaged.passUnretained(self).toOpaque()
                )
                IOHIDDeviceScheduleWithRunLoop(device, CFRunLoopGetCurrent(), CFRunLoopMode.defaultMode.rawValue)
            }
        }
    }

    fileprivate func handleReport(result: IOReturn, type: IOHIDReportType, reportId: UInt32, report: UnsafeMutablePointer<UInt8>?, length: CFIndex) {
        guard result == kIOReturnSuccess, type == kIOHIDReportTypeInput, let report, length > 0 else {
            logger.event(source: "usb.iohid", fields: [
                "result": result,
                "reportType": type.rawValue,
                "reportId": reportId,
                "length": length
            ])
            return
        }

        var bytes = Array(UnsafeBufferPointer(start: report, count: Int(length)))
        if reportId != 0, bytes.first != UInt8(reportId & 0xff) {
            bytes.insert(UInt8(reportId & 0xff), at: 0)
        }

        logger.event(source: "usb.iohid", fields: [
            "reportId": reportId,
            "length": bytes.count,
            "hex": bytes.hexString
        ])
    }
}

private final class USBDeviceRegistration {
    let device: IOHIDDevice
    let buffer: UnsafeMutablePointer<UInt8>
    let reportLength: CFIndex

    init(device: IOHIDDevice, reportLength: Int) {
        self.device = device
        self.reportLength = CFIndex(reportLength)
        self.buffer = UnsafeMutablePointer<UInt8>.allocate(capacity: reportLength)
    }

    deinit {
        buffer.deallocate()
    }
}

private let usbReportCallback: IOHIDReportCallback = { context, result, _, type, reportId, report, reportLength in
    guard let context else {
        return
    }

    let capture = Unmanaged<USBHIDCapture>.fromOpaque(context).takeUnretainedValue()
    capture.handleReport(result: result, type: type, reportId: reportId, report: report, length: reportLength)
}

private final class BluetoothCapture: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private let logger: CaptureLogger
    private let queue = DispatchQueue(label: "WH851 Bluetooth capture")
    private let ready = DispatchSemaphore(value: 0)
    private var central: CBCentralManager?
    private var peripheral: CBPeripheral?
    private var command: CBCharacteristic?
    private var readySignaled = false
    private var keepAliveTimer: DispatchSourceTimer?

    init(logger: CaptureLogger) {
        self.logger = logger
        super.init()
    }

    func start() {
        queue.async {
            self.central = CBCentralManager(delegate: self, queue: self.queue)
        }
    }

    func stop() {
        queue.async {
            self.keepAliveTimer?.cancel()
            self.keepAliveTimer = nil
            if let peripheral = self.peripheral {
                self.central?.cancelPeripheralConnection(peripheral)
            }
            self.central?.stopScan()
            self.central = nil
            self.peripheral = nil
        }
    }

    func waitReady(timeout seconds: TimeInterval) -> Bool {
        ready.wait(timeout: .now() + seconds) == .success
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        logger.status("Bluetooth central state", fields: [
            "phase": CapturePhase.bluetooth.rawValue,
            "source": "bluetooth.corebluetooth",
            "state": central.state.rawValue
        ])

        guard central.state == .poweredOn else {
            return
        }

        let connected = central.retrieveConnectedPeripherals(withServices: [reportService])
        if let peripheral = connected.first {
            connect(peripheral)
            return
        }

        let hidIdentifiers = findBluetoothPeripheralIdentifiers()
        let retrieved = central.retrievePeripherals(withIdentifiers: hidIdentifiers)
        if let peripheral = retrieved.first {
            connect(peripheral)
            return
        }

        logger.status("Bluetooth scanning for FFE0", fields: [
            "phase": CapturePhase.bluetooth.rawValue,
            "source": "bluetooth.corebluetooth"
        ])
        central.scanForPeripherals(withServices: [reportService], options: [CBCentralManagerScanOptionAllowDuplicatesKey: false])
    }

    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral, advertisementData: [String: Any], rssi RSSI: NSNumber) {
        logger.status("Bluetooth discovered peripheral", fields: [
            "phase": CapturePhase.bluetooth.rawValue,
            "source": "bluetooth.corebluetooth",
            "identifier": peripheral.identifier.uuidString,
            "name": peripheral.name ?? "",
            "rssi": RSSI.intValue
        ])
        connect(peripheral)
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        logger.status("Bluetooth connected", fields: [
            "phase": CapturePhase.bluetooth.rawValue,
            "source": "bluetooth.corebluetooth",
            "identifier": peripheral.identifier.uuidString,
            "name": peripheral.name ?? ""
        ])
        peripheral.discoverServices([reportService])
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        logger.status("Bluetooth connect failed", fields: [
            "phase": CapturePhase.bluetooth.rawValue,
            "source": "bluetooth.corebluetooth",
            "identifier": peripheral.identifier.uuidString,
            "error": error?.localizedDescription ?? ""
        ])
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        logger.status("Bluetooth disconnected", fields: [
            "phase": CapturePhase.bluetooth.rawValue,
            "source": "bluetooth.corebluetooth",
            "identifier": peripheral.identifier.uuidString,
            "error": error?.localizedDescription ?? ""
        ])
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        if let error {
            logger.status("Bluetooth service discovery failed", fields: ["error": error.localizedDescription])
            return
        }

        for service in peripheral.services ?? [] where service.uuid == reportService {
            peripheral.discoverCharacteristics([reportCharacteristic, commandCharacteristic], for: service)
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        if let error {
            logger.status("Bluetooth characteristic discovery failed", fields: ["error": error.localizedDescription])
            return
        }

        for characteristic in service.characteristics ?? [] {
            guard characteristic.uuid == reportCharacteristic || characteristic.uuid == commandCharacteristic else {
                continue
            }

            if characteristic.uuid == commandCharacteristic {
                command = characteristic
            }
            peripheral.setNotifyValue(true, for: characteristic)
            if characteristic.properties.contains(.read) {
                peripheral.readValue(for: characteristic)
            }
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateNotificationStateFor characteristic: CBCharacteristic, error: Error?) {
        if let error {
            logger.status("Bluetooth notify failed", fields: [
                "source": "bluetooth.corebluetooth",
                "characteristic": characteristic.uuid.uuidString,
                "error": error.localizedDescription
            ])
            return
        }

        logger.status("Bluetooth notify", fields: [
            "source": "bluetooth.corebluetooth",
            "characteristic": characteristic.uuid.uuidString,
            "isNotifying": characteristic.isNotifying
        ])

        if !readySignaled,
           let characteristics = characteristic.service?.characteristics,
           characteristics.contains(where: { $0.uuid == reportCharacteristic && $0.isNotifying }),
           characteristics.contains(where: { $0.uuid == commandCharacteristic && $0.isNotifying }) {
            readySignaled = true
            sendStartupCommands()
            startKeepAlive()
            ready.signal()
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        if let error {
            logger.status("Bluetooth update failed", fields: [
                "source": "bluetooth.corebluetooth",
                "characteristic": characteristic.uuid.uuidString,
                "error": error.localizedDescription
            ])
            return
        }

        guard let data = characteristic.value else {
            return
        }

        let original = [UInt8](data)
        if characteristic.uuid == reportCharacteristic {
            var report = original
            if report.count > 1 {
                report.removeFirst()
                report[0] = 0x08
            }

            logger.event(source: "bluetooth.corebluetooth", fields: [
                "characteristic": characteristic.uuid.uuidString,
                "originalHex": original.hexString,
                "hex": report.hexString,
                "length": report.count
            ])
        } else if characteristic.uuid == commandCharacteristic {
            logger.event(source: "bluetooth.corebluetooth.command", fields: [
                "characteristic": characteristic.uuid.uuidString,
                "hex": original.hexString,
                "length": original.count
            ])
        }
    }

    private func connect(_ peripheral: CBPeripheral) {
        central?.stopScan()
        self.peripheral = peripheral
        peripheral.delegate = self
        logger.status("Bluetooth connecting", fields: [
            "phase": CapturePhase.bluetooth.rawValue,
            "source": "bluetooth.corebluetooth",
            "identifier": peripheral.identifier.uuidString,
            "name": peripheral.name ?? ""
        ])
        central?.connect(peripheral, options: nil)
    }

    private func sendStartupCommands() {
        let commands: [UInt8] = [0xC9, 0xC8, 0xCA, 0xD1]
        for (index, commandId) in commands.enumerated() {
            queue.asyncAfter(deadline: .now() + Double(index) * 0.25) { [weak self] in
                self?.writeCommand(commandId)
            }
        }
    }

    private func startKeepAlive() {
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 5, repeating: 5)
        timer.setEventHandler { [weak self] in
            self?.writeCommand(0xD1)
        }
        timer.resume()
        keepAliveTimer = timer
    }

    private func writeCommand(_ commandId: UInt8) {
        guard let peripheral, let command else {
            return
        }

        let bytes: [UInt8] = [0xCD, commandId, 0, 0, 0, 0, 0, 0]
        let type: CBCharacteristicWriteType = command.properties.contains(.writeWithoutResponse) ? .withoutResponse : .withResponse
        peripheral.writeValue(Data(bytes), for: command, type: type)
        logger.status("Bluetooth write command", fields: [
            "source": "bluetooth.corebluetooth",
            "characteristic": command.uuid.uuidString,
            "hex": bytes.hexString
        ])
    }
}

private final class QuartzEventCapture {
    private let logger: CaptureLogger
    private var thread: Thread?
    private var shouldStop = false
    private var runLoopSource: CFRunLoopSource?
    private var eventTap: CFMachPort?

    init(logger: CaptureLogger) {
        self.logger = logger
    }

    func start() {
        shouldStop = false
        thread = Thread { [weak self] in
            self?.run()
        }
        thread?.name = "WH851 Quartz capture"
        thread?.start()
    }

    func stop() {
        shouldStop = true
        Thread.sleep(forTimeInterval: 0.25)
    }

    private func run() {
        let mask = eventMask([
            .leftMouseDown,
            .leftMouseUp,
            .rightMouseDown,
            .rightMouseUp,
            .otherMouseDown,
            .otherMouseUp,
            .tabletProximity,
        ])

        guard let tap = CGEvent.tapCreate(
            tap: CGEventTapLocation(rawValue: 0)!,
            place: .headInsertEventTap,
            options: .listenOnly,
            eventsOfInterest: mask,
            callback: quartzEventCallback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()
        ) ?? CGEvent.tapCreate(
            tap: CGEventTapLocation(rawValue: 1)!,
            place: .headInsertEventTap,
            options: .listenOnly,
            eventsOfInterest: mask,
            callback: quartzEventCallback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()
        ) else {
            logger.status("Quartz event tap unavailable. Grant Input Monitoring to Terminal if needed.", fields: [
                "source": "quartz.eventtap"
            ])
            return
        }

        eventTap = tap
        runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        if let runLoopSource {
            CFRunLoopAddSource(CFRunLoopGetCurrent(), runLoopSource, CFRunLoopMode.commonModes)
        }
        CGEvent.tapEnable(tap: tap, enable: true)
        logger.status("Quartz event tap ready", fields: ["source": "quartz.eventtap"])

        while !shouldStop {
            CFRunLoopRunInMode(CFRunLoopMode.defaultMode, 0.1, false)
        }

        if let runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetCurrent(), runLoopSource, CFRunLoopMode.commonModes)
        }
        if let eventTap {
            CFMachPortInvalidate(eventTap)
        }
    }

    fileprivate func handle(type: CGEventType, event: CGEvent) {
        logger.event(source: "quartz.eventtap", fields: [
            "eventType": type.rawValue,
            "eventName": eventName(type),
            "mouseButtonNumber": event.getIntegerValueField(.mouseEventButtonNumber),
            "mouseClickState": event.getIntegerValueField(.mouseEventClickState),
            "tabletButtons": event.getIntegerValueField(.tabletEventPointButtons),
            "tabletPressure": event.getDoubleValueField(.tabletEventPointPressure),
            "tabletDeviceId": event.getIntegerValueField(.tabletEventDeviceID),
            "proximityEnter": event.getIntegerValueField(.tabletProximityEventEnterProximity)
        ])
    }
}

private let quartzEventCallback: CGEventTapCallBack = { _, type, event, context in
    guard let context else {
        return Unmanaged.passUnretained(event)
    }

    let capture = Unmanaged<QuartzEventCapture>.fromOpaque(context).takeUnretainedValue()
    capture.handle(type: type, event: event)
    return Unmanaged.passUnretained(event)
}

private func findBluetoothPeripheralIdentifiers() -> [UUID] {
    let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
    let matching: [String: Any] = [
        kIOHIDVendorIDKey: vendorId,
        kIOHIDProductIDKey: bluetoothProductId
    ]

    IOHIDManagerSetDeviceMatching(manager, matching as CFDictionary)
    IOHIDManagerOpen(manager, IOOptionBits(kIOHIDOptionsTypeNone))
    defer {
        IOHIDManagerClose(manager, IOOptionBits(kIOHIDOptionsTypeNone))
    }

    guard let devices = IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice> else {
        return []
    }

    return devices.compactMap { device in
        guard let value = IOHIDDeviceGetProperty(device, "PhysicalDeviceUniqueID" as CFString) else {
            return nil
        }
        return UUID(uuidString: "\(value)")
    }
}

private func deviceFields(_ device: IOHIDDevice) -> [String: Any] {
    [
        "source": "usb.iohid",
        "vendorId": intProperty(device, kIOHIDVendorIDKey),
        "productId": intProperty(device, kIOHIDProductIDKey),
        "transport": stringProperty(device, "Transport"),
        "product": stringProperty(device, "Product"),
        "manufacturer": stringProperty(device, "Manufacturer"),
        "serial": stringProperty(device, "SerialNumber"),
        "maxInputReportSize": intProperty(device, kIOHIDMaxInputReportSizeKey),
        "primaryUsagePage": intProperty(device, kIOHIDPrimaryUsagePageKey),
        "primaryUsage": intProperty(device, kIOHIDPrimaryUsageKey)
    ]
}

private func intProperty(_ device: IOHIDDevice, _ key: String) -> Int {
    guard let value = IOHIDDeviceGetProperty(device, key as CFString) else {
        return 0
    }
    return (value as? NSNumber)?.intValue ?? 0
}

private func stringProperty(_ device: IOHIDDevice, _ key: String) -> String {
    guard let value = IOHIDDeviceGetProperty(device, key as CFString) else {
        return ""
    }
    return "\(value)"
}

private func eventMask(_ types: [CGEventType]) -> CGEventMask {
    types.reduce(CGEventMask(0)) { mask, type in
        mask | (CGEventMask(1) << CGEventMask(type.rawValue))
    }
}

private func eventName(_ type: CGEventType) -> String {
    switch type {
    case .leftMouseDown: return "leftMouseDown"
    case .leftMouseUp: return "leftMouseUp"
    case .rightMouseDown: return "rightMouseDown"
    case .rightMouseUp: return "rightMouseUp"
    case .otherMouseDown: return "otherMouseDown"
    case .otherMouseUp: return "otherMouseUp"
    case .tabletProximity: return "tabletProximity"
    default: return "type\(type.rawValue)"
    }
}

private extension Array where Element == UInt8 {
    var hexString: String {
        map { String(format: "%02X", $0) }.joined(separator: "-")
    }
}

private func defaultLogPath() -> String {
    let formatter = DateFormatter()
    formatter.dateFormat = "yyyyMMdd-HHmmss"
    return "logs/wh851-input-capture-\(formatter.string(from: Date())).jsonl"
}

private func parseArguments() -> (logPath: String, usbOnly: Bool, bluetoothOnly: Bool) {
    var logPath = defaultLogPath()
    var usbOnly = false
    var bluetoothOnly = false

    var iterator = CommandLine.arguments.dropFirst().makeIterator()
    while let arg = iterator.next() {
        switch arg {
        case "--log":
            if let value = iterator.next() {
                logPath = value
            }
        case "--usb-only":
            usbOnly = true
        case "--bt-only", "--bluetooth-only":
            bluetoothOnly = true
        case "--help", "-h":
            print("usage: scripts/wh851-input-capture.sh [--log path] [--usb-only|--bt-only]")
            exit(0)
        default:
            fputs("unknown arg: \(arg)\n", stderr)
            exit(2)
        }
    }

    return (logPath, usbOnly, bluetoothOnly)
}

private func waitForReturn(_ prompt: String) {
    print(prompt, terminator: "")
    fflush(stdout)
    _ = readLine()
}

private func runPhase(_ phase: CapturePhase, logger: CaptureLogger, startCapture: () -> Void, stopCapture: () -> Void, ready: (() -> Bool)? = nil) {
    print("")
    print("\(phase.rawValue): connect tablet in this mode, then press Return.")
    _ = readLine()

    logger.status("\(phase.rawValue) phase starting", fields: ["phase": phase.rawValue])
    startCapture()

    if let ready, !ready() {
        print("\(phase.rawValue): listener not ready before timeout. Continuing; log will show why.")
        logger.status("\(phase.rawValue) listener readiness timed out", fields: ["phase": phase.rawValue])
    }

    for (index, step) in captureSteps.enumerated() {
        logger.setStep(phase: phase, step: step, index: index + 1)
        waitForReturn("[\(phase.rawValue)] \(step.name): \(step.instruction), then press Return...")
        logger.clearStep()
    }

    stopCapture()
    logger.status("\(phase.rawValue) phase ended", fields: ["phase": phase.rawValue])
}

let options = parseArguments()

do {
    let logger = try CaptureLogger(logPath: options.logPath)
    let quartz = QuartzEventCapture(logger: logger)
    quartz.start()

    print("WH851 input capture")
    print("Log: \(logger.logPath)")
    print("Quit OpenTabletDriver/official driver first if they steal HID reports.")
    print("Grant Input Monitoring/Bluetooth permission to Terminal if macOS asks.")

    if !options.bluetoothOnly {
        let usb = USBHIDCapture(logger: logger)
        runPhase(.usb, logger: logger, startCapture: {
            usb.start()
            Thread.sleep(forTimeInterval: 1.0)
        }, stopCapture: {
            usb.stop()
        })
    }

    if !options.usbOnly {
        let bluetooth = BluetoothCapture(logger: logger)
        runPhase(.bluetooth, logger: logger, startCapture: {
            bluetooth.start()
        }, stopCapture: {
            bluetooth.stop()
        }, ready: {
            bluetooth.waitReady(timeout: 20)
        })
    }

    quartz.stop()
    logger.status("capture complete")
    print("")
    print("Done. Log written: \(logger.logPath)")
} catch {
    fputs("fatal: \(error)\n", stderr)
    exit(1)
}
