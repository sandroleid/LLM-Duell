// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "LLMDuell",
    platforms: [.macOS(.v15)],
    targets: [
        .executableTarget(name: "LLMDuell", path: "Sources/LLMDuell")
    ],
    swiftLanguageModes: [.v5]
)
