import SwiftUI

extension Side {
    var color: Color {
        self == .left ? Color(red: 0.33, green: 0.52, blue: 1.0) : Color(red: 1.0, green: 0.38, blue: 0.55)
    }

    var secondaryColor: Color {
        self == .left ? Color(red: 0.56, green: 0.38, blue: 1.0) : Color(red: 1.0, green: 0.60, blue: 0.30)
    }

    var gradient: LinearGradient {
        LinearGradient(colors: [color, secondaryColor], startPoint: .topLeading, endPoint: .bottomTrailing)
    }
}

enum Theme {
    static let duelGradient = LinearGradient(
        colors: [Side.left.color, Side.left.secondaryColor, Side.right.color],
        startPoint: .leading, endPoint: .trailing
    )
}

struct CardModifier: ViewModifier {
    var padding: CGFloat
    var radius: CGFloat

    func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: radius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: radius, style: .continuous)
                    .strokeBorder(Color.primary.opacity(0.07), lineWidth: 1)
            )
            .shadow(color: .black.opacity(0.06), radius: 8, y: 3)
    }
}

extension View {
    func card(padding: CGFloat = 14, radius: CGFloat = 16) -> some View {
        modifier(CardModifier(padding: padding, radius: radius))
    }
}

struct IconButton: View {
    let systemImage: String
    let help: String
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemImage)
                .font(.system(size: 13, weight: .semibold))
                .frame(width: 32, height: 32)
                .background(.regularMaterial, in: Circle())
                .overlay(Circle().strokeBorder(Color.primary.opacity(hovering ? 0.18 : 0.07)))
                .scaleEffect(hovering ? 1.05 : 1)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help(help)
        .onHover { hovering = $0 }
        .animation(.snappy(duration: 0.15), value: hovering)
    }
}

struct Chip: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.caption2.weight(.medium))
            .foregroundStyle(.secondary)
            .padding(.horizontal, 7)
            .padding(.vertical, 2)
            .background(Color.primary.opacity(0.06), in: Capsule())
    }
}

enum Format {
    static func tps(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(1)))
    }

    static func seconds(_ value: Double?) -> String {
        guard let value else { return "–" }
        if value < 1 { return "\(Int((value * 1000).rounded())) ms" }
        return value.formatted(.number.precision(.fractionLength(value < 10 ? 2 : 1))) + " s"
    }

    static func tokens(_ value: Int?) -> String {
        guard let value else { return "–" }
        return value.formatted(.number)
    }
}
