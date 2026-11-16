// swift-tools-version:5.7
import PackageDescription

let package = Package(
    name: "DualCursor",
    platforms: [.macOS(.v12)],
    targets: [
        .executableTarget(name: "DualCursor", path: "Sources/DualCursor")
    ]
)
