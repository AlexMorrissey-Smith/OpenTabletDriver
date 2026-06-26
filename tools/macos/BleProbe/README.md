# BleProbe

Generic macOS CoreBluetooth probe for scanning, connecting, discovering services and characteristics, subscribing to notifications, and dumping raw BLE packets.

Build:

```sh
tools/macos/BleProbe/build.sh
```

Examples:

```sh
tools/macos/BleProbe/.build/release/BleProbe scan --duration 20 --name-contains WH851
tools/macos/BleProbe/.build/release/BleProbe scan --duration 45 --name-contains WH851 --connect --subscribe
tools/macos/BleProbe/.build/release/BleProbe connected --services 1812 --name-contains WH851 --connect --subscribe
```

`WH851` is only an example filter. The probe is device-generic.
