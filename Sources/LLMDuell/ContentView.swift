import SwiftUI

struct ContentView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let scale = ChartScale(app.left, app.right)
        ZStack {
            BackgroundView()
            VStack(spacing: 14) {
                HeaderView()
                HStack(alignment: .top, spacing: 16) {
                    ModelPanel(run: app.left, scale: scale)
                    ModelPanel(run: app.right, scale: scale)
                }
                ComparisonBar()
                PromptBar()
            }
            .padding(.horizontal, 18)
            .padding(.bottom, 18)
        }
    }
}

/// Gemeinsame Achsen für beide Diagramme, damit die Kurven direkt vergleichbar sind.
struct ChartScale {
    let maxY: Double
    let maxX: Double

    @MainActor init(_ a: ModelRun, _ b: ModelRun) {
        let peak = (a.samples + b.samples).map(\.tps).max() ?? 0
        maxY = max(10, max(peak, a.displayTPS, b.displayTPS) * 1.15)
        maxX = max(2, (a.samples.last?.t ?? 0), (b.samples.last?.t ?? 0))
    }
}

private struct BackgroundView: View {
    var body: some View {
        ZStack {
            Color(nsColor: .windowBackgroundColor)
            RadialGradient(colors: [Side.left.color.opacity(0.16), .clear], center: .topLeading, startRadius: 0, endRadius: 750)
            RadialGradient(colors: [Side.right.color.opacity(0.14), .clear], center: .bottomTrailing, startRadius: 0, endRadius: 750)
        }
        .ignoresSafeArea()
    }
}

private struct HeaderView: View {
    @Environment(AppModel.self) private var app
    @State private var showOptions = false

    var body: some View {
        HStack(spacing: 12) {
            ZStack {
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(LinearGradient(colors: [Side.left.color, Side.right.color], startPoint: .topLeading, endPoint: .bottomTrailing))
                Image(systemName: "bolt.fill")
                    .font(.system(size: 14, weight: .bold))
                    .foregroundStyle(.white)
            }
            .frame(width: 30, height: 30)
            .shadow(color: Side.left.color.opacity(0.35), radius: 6, y: 2)

            VStack(alignment: .leading, spacing: 0) {
                Text("LLM Duell").font(.system(.headline, design: .rounded))
                Text("Lokale Modelle im direkten Vergleich").font(.caption).foregroundStyle(.secondary)
            }

            Spacer()

            ConnectionPill()

            IconButton(systemImage: "arrow.left.arrow.right", help: "Modelle tauschen (⇧⌘T)") { app.swapModels() }
                .disabled(app.isRunning)
            IconButton(systemImage: "arrow.clockwise", help: "Modellliste aktualisieren (⌘R)") {
                Task { await app.refreshModels() }
            }
            IconButton(systemImage: "slider.horizontal.3", help: "Optionen") { showOptions.toggle() }
                .popover(isPresented: $showOptions, arrowEdge: .bottom) { OptionsView() }
        }
        .frame(height: 44)
        .contentShape(Rectangle())
        .gesture(WindowDragGesture())
    }
}

private struct ConnectionPill: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let (color, text, help): (Color, String, String) = switch app.connection {
        case .checking:
            (.orange, "Verbinde …", app.host)
        case .connected(let version):
            (.green, "Ollama \(version) · \(app.models.count) \(app.models.count == 1 ? "Modell" : "Modelle")", app.host)
        case .failed(let message):
            (.red, "Ollama nicht erreichbar", message)
        }
        HStack(spacing: 7) {
            Circle()
                .fill(color)
                .frame(width: 7, height: 7)
                .shadow(color: color.opacity(0.8), radius: 3)
            Text(text)
                .font(.caption.weight(.medium))
                .monospacedDigit()
        }
        .padding(.horizontal, 11)
        .padding(.vertical, 6)
        .background(.regularMaterial, in: Capsule())
        .overlay(Capsule().strokeBorder(Color.primary.opacity(0.07)))
        .help(help)
    }
}
