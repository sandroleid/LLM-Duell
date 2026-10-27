import SwiftUI

struct PromptBar: View {
    @Environment(AppModel.self) private var app
    @FocusState private var focused: Bool

    var body: some View {
        @Bindable var app = app

        HStack(alignment: .bottom, spacing: 10) {
            TextField("Prompt eingeben – beide Modelle erhalten denselben Text", text: $app.prompt, axis: .vertical)
                .textFieldStyle(.plain)
                .font(.system(size: 14))
                .lineLimit(1...6)
                .focused($focused)
                .onSubmit { app.start() }
                .padding(.vertical, 13)
                .padding(.horizontal, 16)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                .overlay(
                    RoundedRectangle(cornerRadius: 14, style: .continuous)
                        .strokeBorder(focused ? AnyShapeStyle(Theme.duelGradient.opacity(0.7)) : AnyShapeStyle(Color.primary.opacity(0.08)),
                                      lineWidth: focused ? 1.5 : 1)
                )
                .animation(.easeInOut(duration: 0.2), value: focused)

            Picker("Ablauf", selection: $app.mode) {
                ForEach(RunMode.allCases) { Text($0.rawValue).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            .disabled(app.isRunning)
            .help("Nacheinander misst fairer, weil sich die Modelle GPU und Speicher nicht teilen.")
            .padding(.bottom, 9)

            if app.isRunning {
                Button { app.stop() } label: {
                    Label("Stopp", systemImage: "stop.fill")
                }
                .buttonStyle(GradientButtonStyle(gradient: LinearGradient(colors: [.red, .orange], startPoint: .leading, endPoint: .trailing)))
                .keyboardShortcut(.escape, modifiers: [])
            } else {
                Button { app.start() } label: {
                    Label("Vergleichen", systemImage: "play.fill")
                }
                .buttonStyle(GradientButtonStyle(gradient: Theme.duelGradient))
                .disabled(!app.canStart)
            }
        }
        .onAppear { focused = true }
    }
}

private struct GradientButtonStyle: ButtonStyle {
    let gradient: LinearGradient
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(.body, design: .rounded).weight(.semibold))
            .foregroundStyle(.white)
            .padding(.horizontal, 20)
            .frame(height: 46)
            .background(gradient, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .shadow(color: Side.left.color.opacity(isEnabled ? 0.35 : 0), radius: 8, y: 3)
            .opacity(isEnabled ? 1 : 0.45)
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.snappy(duration: 0.15), value: configuration.isPressed)
    }
}
