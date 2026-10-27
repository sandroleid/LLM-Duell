import SwiftUI

struct ComparisonBar: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let a = app.left, b = app.right
        let hasData = a.displayTPS > 0 || b.displayTPS > 0

        VStack(spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                speedLabel(a)
                Spacer()
                verdict
                Spacer()
                speedLabel(b)
            }
            if hasData {
                DuelBar(a: a.displayTPS, b: b.displayTPS)
                    .frame(height: 8)
            }
            if let sa = a.stats, let sb = b.stats {
                HStack(spacing: 18) {
                    compare("Erstes Token", Format.seconds(sa.timeToFirstToken), Format.seconds(sb.timeToFirstToken))
                    compare("Output-Tokens", Format.tokens(sa.outputTokens), Format.tokens(sb.outputTokens))
                    compare("Tokens gesamt", Format.tokens(sa.totalTokens), Format.tokens(sb.totalTokens))
                    compare("Gesamtdauer", Format.seconds(sa.totalSeconds), Format.seconds(sb.totalSeconds))
                }
                .font(.caption)
                .transition(.opacity.combined(with: .move(edge: .bottom)))
            }
        }
        .card(padding: 14)
        .animation(.snappy, value: a.stats != nil && b.stats != nil)
    }

    private func speedLabel(_ run: ModelRun) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            Text(Format.tps(run.displayTPS))
                .font(.system(.title3, design: .rounded).weight(.bold))
                .foregroundStyle(run.side.gradient)
                .contentTransition(.numericText(value: run.displayTPS))
            Text("tok/s").font(.caption).foregroundStyle(.secondary)
        }
        .monospacedDigit()
        .frame(width: 140, alignment: run.side == .left ? .leading : .trailing)
    }

    @ViewBuilder private var verdict: some View {
        let a = app.left, b = app.right
        if let sa = a.stats, let sb = b.stats, sa.tokensPerSecond > 0, sb.tokensPerSecond > 0 {
            let leftWins = sa.tokensPerSecond >= sb.tokensPerSecond
            let winner = leftWins ? a : b
            let ratio = max(sa.tokensPerSecond, sb.tokensPerSecond) / min(sa.tokensPerSecond, sb.tokensPerSecond)
            let name = a.modelName == b.modelName ? "Modell \(winner.side.letter)" : winner.modelName
            if ratio < 1.03 {
                Label("Praktisch gleich schnell", systemImage: "equal.circle.fill")
                    .font(.system(.headline, design: .rounded))
            } else {
                HStack(spacing: 6) {
                    Image(systemName: "bolt.fill").foregroundStyle(winner.side.gradient)
                    Text(name).foregroundStyle(winner.side.gradient)
                    Text("ist \(ratio.formatted(.number.precision(.fractionLength(2))))× schneller")
                }
                .font(.system(.headline, design: .rounded))
            }
        } else if app.isRunning {
            HStack(spacing: 8) {
                ProgressView().controlSize(.small)
                Text("Vergleich läuft …").foregroundStyle(.secondary)
            }
            .font(.callout)
        } else if app.models.count < 2, case .connected = app.connection {
            Text("Nur ein Modell installiert – weitere z. B. mit „ollama pull llama3.2“ laden.")
                .font(.callout)
                .foregroundStyle(.secondary)
        } else {
            Text("Wähle zwei Modelle und starte einen Vergleich.")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
    }

    private func compare(_ title: String, _ a: String, _ b: String) -> some View {
        HStack(spacing: 5) {
            Text(title).foregroundStyle(.tertiary)
            Text(a).foregroundStyle(Side.left.color).fontWeight(.semibold)
            Text("vs").foregroundStyle(.tertiary)
            Text(b).foregroundStyle(Side.right.color).fontWeight(.semibold)
        }
        .monospacedDigit()
        .lineLimit(1)
    }
}

private struct DuelBar: View {
    let a: Double
    let b: Double

    var body: some View {
        GeometryReader { geo in
            let total = max(a + b, 0.0001)
            // Jede Seite behält mindestens 4 pt, damit beide Farben sichtbar bleiben.
            let flexible = max(0, geo.size.width - 3 - 8)
            let share = (a == 0 && b == 0) ? 0.5 : a / total
            HStack(spacing: 3) {
                Capsule().fill(Side.left.gradient)
                    .frame(width: 4 + flexible * share)
                Capsule().fill(Side.right.gradient)
                    .frame(width: 4 + flexible * (1 - share))
            }
            .animation(.smooth(duration: 0.4), value: share)
        }
    }
}
