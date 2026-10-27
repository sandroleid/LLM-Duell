import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

@main
struct LLMDuellApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @State private var app = AppModel()

    var body: some Scene {
        Window("LLM Duell", id: "main") {
            ContentView()
                .environment(app)
                .frame(minWidth: 1000, minHeight: 700)
                .task {
                    #if DEBUG
                    DebugSnapshot.startIfRequested(app: app)
                    #endif
                    await app.refreshModels()
                }
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1320, height: 900)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandMenu("Vergleich") {
                Button("Vergleichen") { app.start() }
                    .keyboardShortcut(.return, modifiers: .command)
                    .disabled(!app.canStart)
                Button("Stoppen") { app.stop() }
                    .keyboardShortcut(".", modifiers: .command)
                    .disabled(!app.isRunning)
                Divider()
                Button("Modelle tauschen") { app.swapModels() }
                    .keyboardShortcut("t", modifiers: [.command, .shift])
                    .disabled(app.isRunning)
                Button("Modellliste aktualisieren") { Task { await app.refreshModels() } }
                    .keyboardShortcut("r", modifiers: .command)
                Divider()
                Button("Session-Statistik zurücksetzen") { app.resetSession() }
            }
        }
    }
}
