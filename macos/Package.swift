// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "PolarH10Mac",
    platforms: [
        .macOS(.v13),
    ],
    products: [
        .executable(name: "PolarH10Mac", targets: ["PolarH10Mac"]),
        .library(name: "PolarH10MacCore", targets: ["PolarH10MacCore"]),
    ],
    targets: [
        .target(name: "PolarH10MacCore"),
        .executableTarget(
            name: "PolarH10Mac",
            dependencies: ["PolarH10MacCore"]
        ),
        .testTarget(
            name: "PolarH10MacCoreTests",
            dependencies: ["PolarH10MacCore"]
        ),
    ]
)
