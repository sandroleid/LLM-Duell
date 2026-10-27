import SwiftUI

struct OptionsView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        @Bindable var app = app

        Form {
            Section("Verbindung") {
                TextField("Ollama-Host", text: $app.host)
                    .onSubmit { Task { await app.refreshModels() } }
                Button("Verbinden & Modelle laden") { Task { await app.refreshModels() } }
            }

            Section {
                Picker("Ablauf", selection: $app.mode) {
                    ForEach(RunMode.allCases) { Text($0.rawValue).tag($0) }
                }
                LabeledContent("Temperatur") {
                    HStack {
                        Slider(value: $app.temperature, in: 0...2, step: 0.05)
                        Text(app.temperature.formatted(.number.precision(.fractionLength(2))))
                            .monospacedDigit()
                            .frame(width: 36, alignment: .trailing)
                    }
                }
                Toggle("Fester Seed", isOn: $app.useSeed)
                if app.useSeed {
                    TextField("Seed", value: $app.seed, format: .number.grouping(.never))
                }
            } header: {
                Text("Generierung")
            } footer: {
                Text("„Nacheinander“ misst fairer, da sich die Modelle GPU und Speicher nicht teilen. Ein fester Seed macht Läufe reproduzierbar.")
                    .foregroundStyle(.secondary)
            }

            Section("System-Prompt (optional)") {
                TextEditor(text: $app.systemPrompt)
                    .font(.body)
                    .frame(height: 70)
            }

            Section {
                Button("Session-Statistik zurücksetzen", role: .destructive) { app.resetSession() }
            }
        }
        .formStyle(.grouped)
        .frame(width: 400)
        .fixedSize(horizontal: false, vertical: true)
        .disabled(app.isRunning)
    }
}
