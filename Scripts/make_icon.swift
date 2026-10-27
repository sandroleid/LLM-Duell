// Erzeugt ein .iconset für LLM Duell. Aufruf: swift Scripts/make_icon.swift <ziel.iconset>
import AppKit

let output = URL(fileURLWithPath: CommandLine.arguments[1])
try? FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

func render(size: Int) -> Data {
    let s = CGFloat(size)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)

    // macOS-Raster: Inhalt 824/1024 mit abgerundeten Ecken.
    let inset = s * 100 / 1024
    let rect = NSRect(x: inset, y: inset, width: s - 2 * inset, height: s - 2 * inset)
    let shape = NSBezierPath(roundedRect: rect, xRadius: rect.width * 0.225, yRadius: rect.width * 0.225)

    NSGraphicsContext.current?.saveGraphicsState()
    let shadow = NSShadow()
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.3)
    shadow.shadowBlurRadius = s * 0.02
    shadow.shadowOffset = NSSize(width: 0, height: -s * 0.01)
    shadow.set()
    NSColor.black.setFill()
    shape.fill()
    NSGraphicsContext.current?.restoreGraphicsState()

    NSGradient(colors: [
        NSColor(red: 0.33, green: 0.52, blue: 1.0, alpha: 1),
        NSColor(red: 0.56, green: 0.38, blue: 1.0, alpha: 1),
        NSColor(red: 1.0, green: 0.38, blue: 0.55, alpha: 1),
    ])!.draw(in: shape, angle: -45)

    // Zwei Balken als Symbol für den Vergleich, dazu ein Blitz.
    NSColor.white.withAlphaComponent(0.92).setFill()
    let barW = rect.width * 0.16
    let base = rect.minY + rect.height * 0.2
    NSBezierPath(roundedRect: NSRect(x: rect.minX + rect.width * 0.22, y: base, width: barW, height: rect.height * 0.38),
                 xRadius: barW / 2, yRadius: barW / 2).fill()
    NSBezierPath(roundedRect: NSRect(x: rect.maxX - rect.width * 0.22 - barW, y: base, width: barW, height: rect.height * 0.58),
                 xRadius: barW / 2, yRadius: barW / 2).fill()

    let config = NSImage.SymbolConfiguration(pointSize: rect.width * 0.34, weight: .heavy)
        .applying(.init(paletteColors: [.white]))
    if let bolt = NSImage(systemSymbolName: "bolt.fill", accessibilityDescription: nil)?.withSymbolConfiguration(config) {
        let b = bolt.size
        bolt.draw(in: NSRect(x: rect.midX - b.width / 2, y: rect.minY + rect.height * 0.5, width: b.width, height: b.height))
    }

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

for base in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let name = scale == 1 ? "icon_\(base)x\(base).png" : "icon_\(base)x\(base)@2x.png"
        try render(size: base * scale).write(to: output.appendingPathComponent(name))
    }
}
