#if DEBUG
import AppKit

/// Nur in Debug-Builds: startet per Umgebungsvariablen einen Testlauf und speichert Fenster-Snapshots.
enum DebugSnapshot {
    @MainActor static func startIfRequested(app: AppModel) {
        let env = ProcessInfo.processInfo.environment
        guard let dir = env["LLMDUELL_SNAPSHOT_DIR"] else { return }
        if env["LLMDUELL_APPEARANCE"] == "dark" { NSApp.appearance = NSAppearance(named: .darkAqua) }
        if env["LLMDUELL_APPEARANCE"] == "light" { NSApp.appearance = NSAppearance(named: .aqua) }
        let shots = Int(env["LLMDUELL_SHOTS"] ?? "") ?? 5
        let interval = Double(env["LLMDUELL_INTERVAL"] ?? "") ?? 3

        Task {
            try? await Task.sleep(for: .seconds(1.5))
            if let prompt = env["LLMDUELL_PROMPT"] {
                app.prompt = prompt
                app.start()
            }
            for index in 0..<shots {
                try? await Task.sleep(for: .seconds(index == 0 ? 0.5 : interval))
                guard let window = NSApp.windows.first(where: \.isVisible),
                      let view = window.contentView?.superview ?? window.contentView,
                      let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { continue }
                view.cacheDisplay(in: view.bounds, to: rep)
                let url = URL(fileURLWithPath: dir).appendingPathComponent("shot\(index).png")
                try? rep.representation(using: .png, properties: [:])?.write(to: url)
            }
            if env["LLMDUELL_QUIT"] == "1" { NSApp.terminate(nil) }
        }
    }
}
#endif
