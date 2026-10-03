// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "KeyShadow",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "KeyShadow", targets: ["KeyShadow"])],
    targets: [
        .target(name: "KeyShadowCore", resources: [.process("Resources")]),
        .executableTarget(name: "KeyShadow", dependencies: ["KeyShadowCore"]),
        .testTarget(name: "KeyShadowCoreTests", dependencies: ["KeyShadowCore"])
    ]
)
