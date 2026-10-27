import Charts
import SwiftUI

struct ModelPanel: View {
    @Environment(AppModel.self) private var app
    @Bindable var run: ModelRun
    let scale: ChartScale

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            header
            HStack(spacing: 12) {
                speedTile
                tokenTile
            }
            .fixedSize(horizontal: false, vertical: true)
            metrics
            SpeedChart(run: run, scale: scale)
                .frame(height: 120)
                .card(padding: 12)
            OutputView(run: run)
            sessionFooter
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
    }

    // MARK: Kopfzeile

    private var header: some View {
        HStack(alignment: .center, spacing: 10) {
            Text(run.side.letter)
                .font(.system(size: 14, weight: .heavy, design: .rounded))
                .foregroundStyle(.white)
                .frame(width: 30, height: 30)
                .background(run.side.gradient, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                .shadow(color: run.side.color.opacity(0.4), radius: 5, y: 2)

            VStack(alignment: .leading, spacing: 3) {
                modelMenu
                if let model = app.model(named: run.modelName) {
                    HStack(spacing: 4) {
                        if let size = model.details?.parameter_size { Chip(text: size) }
                        if let quant = model.details?.quantization_level { Chip(text: quant) }
                        Chip(text: model.sizeLabel)
                    }
                }
            }

            Spacer(minLength: 8)
            PhaseBadge(run: run)
        }
    }

    private var modelMenu: some View {
        Menu {
            Picker("Modell", selection: $run.modelName) {
                ForEach(app.models) { model in
                    Text(model.name).tag(model.name)
                }
            }
            .pickerStyle(.inline)
            .labelsHidden()
        } label: {
            HStack(spacing: 6) {
                Text(run.modelName.isEmpty ? "Modell wählen" : run.modelName)
                    .font(.system(.title3, design: .rounded).weight(.semibold))
                    .lineLimit(1)
                Image(systemName: "chevron.up.chevron.down")
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(.secondary)
            }
            .contentShape(Rectangle())
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .fixedSize()
        .disabled(app.isRunning || app.models.isEmpty)
    }

    // MARK: Kennzahlen

    private var speedTile: some View {
        VStack(alignment: .leading, spacing: 4) {
            Label("Geschwindigkeit", systemImage: "speedometer")
                .font(.caption.weight(.medium))
                .foregroundStyle(.secondary)
            HStack(alignment: .firstTextBaseline, spacing: 5) {
                Text(Format.tps(run.displayTPS))
                    .font(.system(size: 42, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(run.side.gradient)
                    .contentTransition(.numericText(value: run.displayTPS))
                    .animation(.smooth(duration: 0.25), value: Int(run.displayTPS * 10))
                Text("tok/s")
                    .font(.system(.headline, design: .rounded))
                    .foregroundStyle(.secondary)
            }
            Text(run.stats != nil ? "Gemessen von Ollama (ohne Ladezeit)" : run.isActive ? "Live-Schätzung" : "Noch keine Messung")
                .font(.caption2)
                .foregroundStyle(.tertiary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .card()
    }

    private var tokenTile: some View {
        VStack(alignment: .leading, spacing: 4) {
            Label("Tokens", systemImage: "number")
                .font(.caption.weight(.medium))
                .foregroundStyle(.secondary)
            HStack(alignment: .firstTextBaseline, spacing: 5) {
                Text(Format.tokens(run.displayOutputTokens))
                    .font(.system(size: 42, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .contentTransition(.numericText(value: Double(run.displayOutputTokens)))
                    .animation(.smooth(duration: 0.2), value: run.displayOutputTokens)
                Text("Output")
                    .font(.system(.headline, design: .rounded))
                    .foregroundStyle(.secondary)
            }
            HStack(spacing: 12) {
                tokenDetail("Prompt", Format.tokens(run.stats?.promptTokens))
                tokenDetail("Gesamt", Format.tokens(run.stats?.totalTokens))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .card()
    }

    private func tokenDetail(_ title: String, _ value: String) -> some View {
        HStack(spacing: 4) {
            Text(title).foregroundStyle(.tertiary)
            Text(value).foregroundStyle(.secondary).monospacedDigit()
        }
        .font(.caption2.weight(.medium))
    }

    private var metrics: some View {
        TimelineView(.periodic(from: .now, by: 0.1)) { context in
            HStack(spacing: 10) {
                MetricTile(title: "Erstes Token", icon: "timer", value: Format.seconds(run.timeToFirstToken))
                MetricTile(title: "Ladezeit", icon: "shippingbox", value: Format.seconds(run.stats?.loadSeconds))
                MetricTile(
                    title: "Prompt-Verarb.", icon: "text.magnifyingglass",
                    value: run.stats?.promptTokensPerSecond.map { Format.tps($0) + " tok/s" } ?? "–"
                )
                MetricTile(title: "Dauer", icon: "clock", value: Format.seconds(run.elapsed(at: context.date)))
            }
        }
    }

    private var sessionFooter: some View {
        HStack(spacing: 6) {
            Image(systemName: "sum")
            Text("Session:").fontWeight(.semibold)
            Text("\(run.sessionRuns) \(run.sessionRuns == 1 ? "Lauf" : "Läufe")")
            Text("·")
            Text("\(Format.tokens(run.sessionTotalTokens)) Tokens gesamt")
            Text("(\(Format.tokens(run.sessionPromptTokens)) in / \(Format.tokens(run.sessionOutputTokens)) out)")
                .foregroundStyle(.tertiary)
            Spacer()
            if run.sessionRuns > 0 {
                Text("Ø \(Format.tps(run.sessionAverageTPS)) tok/s")
                    .fontWeight(.semibold)
                    .foregroundStyle(run.side.color)
            }
        }
        .font(.caption)
        .monospacedDigit()
        .foregroundStyle(.secondary)
        .lineLimit(1)
        .padding(.horizontal, 4)
    }
}

private struct MetricTile: View {
    let title: String
    let icon: String
    let value: String

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Label(title, systemImage: icon)
                .font(.caption2.weight(.medium))
                .foregroundStyle(.secondary)
                .lineLimit(1)
            Text(value)
                .font(.system(.callout, design: .rounded).weight(.semibold))
                .monospacedDigit()
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 10, radius: 12)
    }
}

private struct PhaseBadge: View {
    let run: ModelRun

    var body: some View {
        let (text, icon, color): (String, String, Color) = switch run.phase {
        case .idle: ("Bereit", "circle", .secondary)
        case .waiting: ("Wartet", "hourglass", .secondary)
        case .loading: ("Lädt Modell", "arrow.down.circle", .orange)
        case .thinking: ("Denkt nach", "brain", .purple)
        case .streaming: ("Generiert", "waveform", run.side.color)
        case .done: ("Fertig", "checkmark.circle.fill", .green)
        case .cancelled: ("Abgebrochen", "stop.circle", .secondary)
        case .failed: ("Fehler", "exclamationmark.triangle.fill", .red)
        }
        HStack(spacing: 5) {
            Image(systemName: icon)
                .symbolEffect(.pulse, isActive: run.isActive)
            Text(text)
        }
        .font(.caption.weight(.semibold))
        .foregroundStyle(color)
        .padding(.horizontal, 10)
        .padding(.vertical, 5)
        .background(color.opacity(0.12), in: Capsule())
    }
}

// MARK: Diagramm

private struct SpeedChart: View {
    let run: ModelRun
    let scale: ChartScale

    static func tickStride(for maxX: Double) -> Double {
        [1, 2, 5, 10, 15, 30, 60, 120].first { maxX / $0 <= 7 } ?? 300
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Label("Tokens pro Sekunde über Zeit", systemImage: "waveform.path.ecg")
                    .font(.caption.weight(.medium))
                    .foregroundStyle(.secondary)
                Spacer()
                if let stats = run.stats {
                    Text("Ø \(Format.tps(stats.tokensPerSecond)) tok/s")
                        .font(.caption.weight(.semibold))
                        .monospacedDigit()
                        .foregroundStyle(run.side.color)
                }
            }
            Chart {
                ForEach(run.samples) { sample in
                    AreaMark(x: .value("Zeit", sample.t), y: .value("tok/s", sample.tps))
                        .interpolationMethod(.catmullRom)
                        .foregroundStyle(
                            LinearGradient(colors: [run.side.color.opacity(0.35), run.side.color.opacity(0.02)],
                                           startPoint: .top, endPoint: .bottom)
                        )
                    LineMark(x: .value("Zeit", sample.t), y: .value("tok/s", sample.tps))
                        .interpolationMethod(.catmullRom)
                        .foregroundStyle(run.side.gradient)
                        .lineStyle(StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
                }
                if let stats = run.stats {
                    RuleMark(y: .value("Durchschnitt", stats.tokensPerSecond))
                        .lineStyle(StrokeStyle(lineWidth: 1, dash: [4, 4]))
                        .foregroundStyle(run.side.color.opacity(0.7))
                }
            }
            .chartXScale(domain: 0...scale.maxX)
            .chartYScale(domain: 0...scale.maxY)
            .chartXAxis {
                // Kein Label direkt am rechten Rand, dort würde es abgeschnitten.
                AxisMarks(values: Array(stride(from: 0, to: scale.maxX * 0.95, by: Self.tickStride(for: scale.maxX)))) { value in
                    AxisGridLine().foregroundStyle(Color.primary.opacity(0.06))
                    AxisValueLabel {
                        if let seconds = value.as(Double.self) { Text("\(Int(seconds)) s") }
                    }
                }
            }
            .chartYAxis {
                AxisMarks(position: .leading, values: .automatic(desiredCount: 3)) { value in
                    AxisGridLine().foregroundStyle(Color.primary.opacity(0.06))
                    AxisValueLabel()
                }
            }
            .overlay {
                if run.samples.isEmpty {
                    Text("Noch keine Daten")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
            }
        }
    }
}

// MARK: Ausgabe

private struct OutputView: View {
    let run: ModelRun

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    if !run.thinking.isEmpty {
                        ThinkingBox(text: run.thinking, isActive: run.phase == .thinking)
                    }
                    if run.output.isEmpty {
                        placeholder
                    } else {
                        Text(Self.render(run.output))
                            .font(.system(size: 13.5))
                            .lineSpacing(3)
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    if case .failed(let message) = run.phase, !run.output.isEmpty {
                        errorLabel(message)
                    }
                    Color.clear.frame(height: 1).id("bottom")
                }
                .padding(16)
            }
            .onChange(of: run.output.count + run.thinking.count) {
                if run.isActive { proxy.scrollTo("bottom", anchor: .bottom) }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .strokeBorder(
                    run.isActive ? AnyShapeStyle(run.side.gradient.opacity(0.6)) : AnyShapeStyle(Color.primary.opacity(0.07)),
                    lineWidth: run.isActive ? 1.5 : 1
                )
        )
        .animation(.easeInOut(duration: 0.3), value: run.isActive)
    }

    @ViewBuilder private var placeholder: some View {
        switch run.phase {
        case .failed(let message):
            errorLabel(message)
        case .loading:
            HStack(spacing: 10) {
                ProgressView().controlSize(.small)
                Text("Warte auf das erste Token …").foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, minHeight: 160)
        case .waiting:
            emptyState("hourglass", "Startet, sobald Modell A fertig ist")
        case .cancelled:
            emptyState("stop.circle", "Abgebrochen")
        default:
            emptyState("text.bubble", "Die Antwort erscheint hier")
        }
    }

    private func emptyState(_ icon: String, _ text: String) -> some View {
        VStack(spacing: 8) {
            Image(systemName: icon)
                .font(.system(size: 26, weight: .light))
                .foregroundStyle(run.side.gradient)
            Text(text).font(.callout).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, minHeight: 160)
    }

    private func errorLabel(_ message: String) -> some View {
        Label(message, systemImage: "exclamationmark.triangle.fill")
            .font(.callout)
            .foregroundStyle(.red)
            .textSelection(.enabled)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(10)
            .background(Color.red.opacity(0.08), in: RoundedRectangle(cornerRadius: 10))
    }

    /// Inline-Markdown rendern; Überschriften werden zu fettem Text.
    static func render(_ text: String) -> AttributedString {
        let prepared = text
            .split(separator: "\n", omittingEmptySubsequences: false)
            .map { line -> String in
                let trimmed = line.drop(while: { $0 == " " })
                guard trimmed.hasPrefix("#") else { return String(line) }
                let title = trimmed.drop(while: { $0 == "#" }).trimmingCharacters(in: .whitespaces)
                return title.isEmpty ? String(line) : "**\(title)**"
            }
            .joined(separator: "\n")
        let options = AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)
        return (try? AttributedString(markdown: prepared, options: options)) ?? AttributedString(text)
    }
}

private struct ThinkingBox: View {
    let text: String
    let isActive: Bool
    @State private var expanded = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Button {
                withAnimation(.snappy) { expanded.toggle() }
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: "brain")
                        .symbolEffect(.pulse, isActive: isActive)
                    Text(isActive ? "Denkt nach …" : "Gedankengang")
                        .fontWeight(.semibold)
                    Text("\(text.count) Zeichen").foregroundStyle(.tertiary)
                    Spacer()
                    Image(systemName: "chevron.right")
                        .rotationEffect(.degrees(expanded ? 90 : 0))
                }
                .font(.caption)
                .foregroundStyle(.secondary)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            if expanded || isActive {
                Text(text)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .padding(10)
        .background(Color.purple.opacity(0.07), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}
