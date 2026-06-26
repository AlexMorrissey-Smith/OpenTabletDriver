import CoreBluetooth
import Foundation

struct Options {
    enum Mode: String {
        case scan
        case connected
    }

    var mode: Mode
    var duration: TimeInterval = 30
    var nameContains: String?
    var services: [CBUUID] = []
    var connect = false
    var subscribe = false
    var allowDuplicates = false
    var outputPath: String?
}

enum CliError: Error, CustomStringConvertible {
    case message(String)

    var description: String {
        switch self {
        case .message(let value):
            return value
        }
    }
}

final class Probe: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private var central: CBCentralManager!
    private let options: Options
    private let deadline: Date
    private var seenPeripherals: Set<UUID> = []
    private var activePeripherals: Set<UUID> = []
    private var packetCount = 0
    private var logFile: FileHandle?
    private var done = false
    private var stateReceived = false

    init(options: Options) throws {
        self.options = options
        self.deadline = Date().addingTimeInterval(options.duration)

        if let outputPath = options.outputPath {
            FileManager.default.createFile(atPath: outputPath, contents: nil)
            self.logFile = try FileHandle(forWritingTo: URL(fileURLWithPath: outputPath))
        }

        super.init()
        write("START mode=\(options.mode.rawValue) duration=\(Int(options.duration)) services=\(formatServices(options.services))")
        central = CBCentralManager(delegate: self, queue: nil)
    }

    deinit {
        try? logFile?.close()
    }

    func run() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 5) { [weak self] in
            guard let self, !self.stateReceived else {
                return
            }

            self.write("ERROR CoreBluetooth did not report central state within 5 seconds")
            self.done = true
        }

        while !done && Date() < deadline {
            RunLoop.main.run(mode: .default, before: Date().addingTimeInterval(0.1))
        }

        if options.mode == .scan {
            central.stopScan()
        }

        write("DONE packets=\(packetCount)")
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        stateReceived = true
        write("STATE central=\(central.state.description)")
        guard central.state == .poweredOn else {
            if central.state != .unknown && central.state != .resetting {
                done = true
            }
            return
        }

        switch options.mode {
        case .scan:
            let scanOptions = [
                CBCentralManagerScanOptionAllowDuplicatesKey: NSNumber(value: options.allowDuplicates)
            ]
            central.scanForPeripherals(withServices: options.services.nilIfEmpty, options: scanOptions)
            write("SCAN services=\(formatServices(options.services)) duration=\(Int(options.duration))")
        case .connected:
            guard !options.services.isEmpty else {
                write("ERROR connected mode requires --services, for example --services 1812")
                done = true
                return
            }

            let peripherals = central.retrieveConnectedPeripherals(withServices: options.services)
            write("CONNECTED count=\(peripherals.count) services=\(formatServices(options.services))")
            for peripheral in peripherals {
                handleCandidate(peripheral, advertisement: [:], rssi: nil)
            }
            if peripherals.isEmpty {
                done = true
            }
        }
    }

    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral, advertisementData: [String: Any], rssi RSSI: NSNumber) {
        handleCandidate(peripheral, advertisement: advertisementData, rssi: RSSI)
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        write("CONNECTED_PERIPHERAL id=\(peripheral.identifier.uuidString) name=\(quote(peripheral.name))")
        peripheral.discoverServices(options.services.nilIfEmpty)
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        write("CONNECT_FAILED id=\(peripheral.identifier.uuidString) name=\(quote(peripheral.name)) error=\(quote(error?.localizedDescription))")
        activePeripherals.remove(peripheral.identifier)
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        write("DISCONNECTED id=\(peripheral.identifier.uuidString) name=\(quote(peripheral.name)) error=\(quote(error?.localizedDescription))")
        activePeripherals.remove(peripheral.identifier)
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        if let error {
            write("DISCOVER_SERVICES_ERROR id=\(peripheral.identifier.uuidString) error=\(quote(error.localizedDescription))")
        }

        for service in peripheral.services ?? [] {
            write("SERVICE peripheral=\(peripheral.identifier.uuidString) uuid=\(service.uuid.uuidString)")
            peripheral.discoverCharacteristics(nil, for: service)
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        if let error {
            write("DISCOVER_CHARACTERISTICS_ERROR id=\(peripheral.identifier.uuidString) service=\(service.uuid.uuidString) error=\(quote(error.localizedDescription))")
        }

        for characteristic in service.characteristics ?? [] {
            write("CHAR peripheral=\(peripheral.identifier.uuidString) service=\(service.uuid.uuidString) uuid=\(characteristic.uuid.uuidString) props=0x\(String(characteristic.properties.rawValue, radix: 16))")
            if options.subscribe && (characteristic.properties.contains(.notify) || characteristic.properties.contains(.indicate)) {
                write("SUBSCRIBE peripheral=\(peripheral.identifier.uuidString) uuid=\(characteristic.uuid.uuidString)")
                peripheral.setNotifyValue(true, for: characteristic)
            }
            if characteristic.properties.contains(.read) {
                peripheral.readValue(for: characteristic)
            }
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateNotificationStateFor characteristic: CBCharacteristic, error: Error?) {
        write("NOTIFY_STATE peripheral=\(peripheral.identifier.uuidString) uuid=\(characteristic.uuid.uuidString) enabled=\(characteristic.isNotifying) error=\(quote(error?.localizedDescription))")
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        if let error {
            write("UPDATE_ERROR peripheral=\(peripheral.identifier.uuidString) uuid=\(characteristic.uuid.uuidString) error=\(quote(error.localizedDescription))")
            return
        }

        guard let data = characteristic.value else {
            write("UPDATE_EMPTY peripheral=\(peripheral.identifier.uuidString) uuid=\(characteristic.uuid.uuidString)")
            return
        }

        packetCount += 1
        write("PACKET index=\(packetCount) peripheral=\(peripheral.identifier.uuidString) service=\(characteristic.service?.uuid.uuidString ?? "?") char=\(characteristic.uuid.uuidString) len=\(data.count) hex=\(data.hexString)")
    }

    private func handleCandidate(_ peripheral: CBPeripheral, advertisement: [String: Any], rssi: NSNumber?) {
        let name = peripheral.name ?? advertisement[CBAdvertisementDataLocalNameKey] as? String
        if let filter = options.nameContains, !(name ?? "").localizedCaseInsensitiveContains(filter) {
            return
        }

        if !options.allowDuplicates && seenPeripherals.contains(peripheral.identifier) {
            return
        }

        seenPeripherals.insert(peripheral.identifier)
        write("DEVICE id=\(peripheral.identifier.uuidString) name=\(quote(name)) rssi=\(rssi?.stringValue ?? "?") advertisement=\(formatAdvertisement(advertisement))")

        guard options.connect else {
            return
        }

        if activePeripherals.insert(peripheral.identifier).inserted {
            peripheral.delegate = self
            central.connect(peripheral, options: nil)
            write("CONNECT id=\(peripheral.identifier.uuidString) name=\(quote(name))")
        }
    }

    private func write(_ line: String) {
        if let data = (line + "\n").data(using: .utf8) {
            FileHandle.standardOutput.write(data)
            try? logFile?.write(contentsOf: data)
        }
    }
}

extension CBManagerState {
    var description: String {
        switch self {
        case .unknown:
            return "unknown"
        case .resetting:
            return "resetting"
        case .unsupported:
            return "unsupported"
        case .unauthorized:
            return "unauthorized"
        case .poweredOff:
            return "poweredOff"
        case .poweredOn:
            return "poweredOn"
        @unknown default:
            return "unknown(\(rawValue))"
        }
    }
}

extension Collection {
    var nilIfEmpty: Self? {
        isEmpty ? nil : self
    }
}

extension Data {
    var hexString: String {
        map { String(format: "%02X", $0) }.joined(separator: "-")
    }
}

private func parseOptions(arguments: [String]) throws -> Options {
    guard let modeArgument = arguments.first, let mode = Options.Mode(rawValue: modeArgument) else {
        throw CliError.message(usage())
    }

    var options = Options(mode: mode)
    var index = 1

    while index < arguments.count {
        let argument = arguments[index]
        switch argument {
        case "--duration":
            index += 1
            options.duration = try parseDouble(arguments, index, argument)
        case "--name-contains":
            index += 1
            options.nameContains = try parseString(arguments, index, argument)
        case "--services":
            index += 1
            options.services = try parseServices(parseString(arguments, index, argument))
        case "--connect":
            options.connect = true
        case "--subscribe":
            options.subscribe = true
            options.connect = true
        case "--allow-duplicates":
            options.allowDuplicates = true
        case "--output":
            index += 1
            options.outputPath = try parseString(arguments, index, argument)
        case "--help", "-h":
            throw CliError.message(usage())
        default:
            throw CliError.message("Unknown argument: \(argument)\n\n\(usage())")
        }
        index += 1
    }

    return options
}

private func parseString(_ arguments: [String], _ index: Int, _ option: String) throws -> String {
    guard index < arguments.count else {
        throw CliError.message("Missing value for \(option)")
    }
    return arguments[index]
}

private func parseDouble(_ arguments: [String], _ index: Int, _ option: String) throws -> Double {
    let value = try parseString(arguments, index, option)
    guard let parsed = Double(value), parsed > 0 else {
        throw CliError.message("Invalid value for \(option): \(value)")
    }
    return parsed
}

private func parseServices(_ value: String) throws -> [CBUUID] {
    let services = value
        .split(separator: ",")
        .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
        .filter { !$0.isEmpty }
        .map(CBUUID.init(string:))

    guard !services.isEmpty else {
        throw CliError.message("--services requires at least one UUID")
    }

    return services
}

private func quote(_ value: String?) -> String {
    guard let value else {
        return "\"\""
    }
    return "\"\(value.replacingOccurrences(of: "\"", with: "\\\""))\""
}

private func formatServices(_ services: [CBUUID]) -> String {
    services.isEmpty ? "*" : services.map(\.uuidString).joined(separator: ",")
}

private func formatAdvertisement(_ advertisement: [String: Any]) -> String {
    if advertisement.isEmpty {
        return "{}"
    }

    let pairs = advertisement.keys.sorted().map { key -> String in
        let value = advertisement[key]
        if let uuids = value as? [CBUUID] {
            return "\(key)=[\(uuids.map(\.uuidString).joined(separator: ","))]"
        }
        if let data = value as? Data {
            return "\(key)=\(data.hexString)"
        }
        return "\(key)=\(String(describing: value))"
    }

    return "{\(pairs.joined(separator: ";"))}"
}

private func usage() -> String {
    """
    Usage:
      BleProbe scan [--duration seconds] [--name-contains text] [--services uuid[,uuid]] [--connect] [--subscribe] [--allow-duplicates] [--output path]
      BleProbe connected --services uuid[,uuid] [--name-contains text] [--connect] [--subscribe] [--output path]

    Examples:
      BleProbe scan --duration 20 --name-contains WH851
      BleProbe scan --duration 45 --name-contains WH851 --connect --subscribe
      BleProbe connected --services 1812 --name-contains WH851 --connect --subscribe
    """
}

do {
    let options = try parseOptions(arguments: Array(CommandLine.arguments.dropFirst()))
    let probe = try Probe(options: options)
    probe.run()
} catch {
    fputs("\(error)\n", stderr)
    exit(2)
}
