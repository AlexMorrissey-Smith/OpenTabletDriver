import CoreBluetooth
import Foundation

private let reportService = CBUUID(string: "FFE0")
private let reportCharacteristic = CBUUID(string: "FFE1")
private let commandCharacteristic = CBUUID(string: "FFE2")

final class Bridge: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private let identifier: UUID
    private var central: CBCentralManager!
    private var peripheral: CBPeripheral?
    private var command: CBCharacteristic?
    private var keepAliveTimer: Timer?
    private var opened = false

    init(identifier: UUID) {
        self.identifier = identifier
        super.init()
        central = CBCentralManager(delegate: self, queue: nil)
    }

    func run() {
        while true {
            RunLoop.main.run(mode: .default, before: Date().addingTimeInterval(0.1))
        }
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        guard central.state == .poweredOn else {
            log("central state is \(central.state.rawValue)")
            return
        }

        let peripherals = central.retrievePeripherals(withIdentifiers: [identifier])
        guard let peripheral = peripherals.first else {
            log("unable to retrieve peripheral \(identifier.uuidString)")
            exit(3)
        }

        self.peripheral = peripheral
        peripheral.delegate = self
        central.connect(peripheral, options: nil)
        log("connecting \(identifier.uuidString) \(peripheral.name ?? "")")
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        log("connected \(peripheral.identifier.uuidString)")
        peripheral.discoverServices([reportService])
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        log("connect failed \(error?.localizedDescription ?? "")")
        exit(4)
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        log("disconnected \(error?.localizedDescription ?? "")")
        exit(5)
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        if let error {
            log("service discovery failed \(error.localizedDescription)")
            exit(6)
        }

        for service in peripheral.services ?? [] where service.uuid == reportService {
            peripheral.discoverCharacteristics([reportCharacteristic, commandCharacteristic], for: service)
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        if let error {
            log("characteristic discovery failed \(error.localizedDescription)")
            exit(7)
        }

        for characteristic in service.characteristics ?? [] {
            if characteristic.uuid == reportCharacteristic || characteristic.uuid == commandCharacteristic {
                if characteristic.uuid == commandCharacteristic {
                    command = characteristic
                }
                peripheral.setNotifyValue(true, for: characteristic)
                if characteristic.properties.contains(.read) {
                    peripheral.readValue(for: characteristic)
                }
            }
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateNotificationStateFor characteristic: CBCharacteristic, error: Error?) {
        if let error {
            log("notify \(characteristic.uuid.uuidString) failed \(error.localizedDescription)")
            return
        }

        if characteristic.uuid == reportCharacteristic || characteristic.uuid == commandCharacteristic {
            log("notify \(characteristic.uuid.uuidString) \(characteristic.isNotifying)")
        }

        if !opened,
           let characteristics = characteristic.service?.characteristics,
           characteristics.contains(where: { $0.uuid == reportCharacteristic && $0.isNotifying }),
           characteristics.contains(where: { $0.uuid == commandCharacteristic && $0.isNotifying }) {
            opened = true
            log("ready")
            sendStartupCommands()
            keepAliveTimer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
                self?.writeCommand(0xD1)
            }
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        if let error {
            log("update \(characteristic.uuid.uuidString) failed \(error.localizedDescription)")
            return
        }

        guard let data = characteristic.value else {
            return
        }

        var bytes = [UInt8](data)
        if characteristic.uuid == reportCharacteristic {
            guard bytes.count > 1 else {
                return
            }

            bytes.removeFirst()
            bytes[0] = 0x08
            emit(bytes)
        } else if characteristic.uuid == commandCharacteristic {
            guard bytes.count > 1 else {
                return
            }

            log("cmd \(bytes.hexString)")
        }
    }

    private func sendStartupCommands() {
        let commands: [UInt8] = [0xC9, 0xC8, 0xCA, 0xD1]
        for (index, command) in commands.enumerated() {
            DispatchQueue.main.asyncAfter(deadline: .now() + Double(index) * 0.25) { [weak self] in
                self?.writeCommand(command)
            }
        }
    }

    private func writeCommand(_ commandId: UInt8) {
        guard let peripheral, let command else {
            return
        }

        let bytes: [UInt8] = [0xCD, commandId, 0, 0, 0, 0, 0, 0]
        let type: CBCharacteristicWriteType = command.properties.contains(.writeWithoutResponse) ? .withoutResponse : .withResponse
        peripheral.writeValue(Data(bytes), for: command, type: type)
        log("write \(bytes.hexString)")
    }

    private func emit(_ bytes: [UInt8]) {
        let hex = bytes.map { String(format: "%02X", $0) }.joined(separator: "-")
        writeStdout("REPORT \(hex)")
    }

    private func log(_ value: String) {
        let data = ("WH851BleBridge: \(value)\n").data(using: .utf8)!
        FileHandle.standardError.write(data)
    }

    private func writeStdout(_ value: String) {
        let data = (value + "\n").data(using: .utf8)!
        FileHandle.standardOutput.write(data)
    }
}

private extension Array where Element == UInt8 {
    var hexString: String {
        map { String(format: "%02X", $0) }.joined(separator: "-")
    }
}

guard let argument = CommandLine.arguments.dropFirst().first,
      let identifier = UUID(uuidString: argument) else {
    fputs("usage: OpenTabletDriver.WH851BleBridge <peripheral-uuid>\n", stderr)
    exit(2)
}

Bridge(identifier: identifier).run()
