// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "BleProbe",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .executable(name: "BleProbe", targets: ["BleProbe"])
    ],
    targets: [
        .executableTarget(name: "BleProbe")
    ]
)
