import Foundation
import Observation

enum Side: String {
    case left, right

    var letter: String { self == .left ? "A" : "B" }
}

struct GenerationStats {
    var promptTokens: Int
    var outputTokens: Int
    var promptEvalSeconds: Double
    var evalSeconds: Double
    var loadSeconds: Double
    var totalSeconds: Double
    var timeToFirstToken: Double?

    var totalTokens: Int { promptTokens + outputTokens }
    var tokensPerSecond: Double { evalSeconds > 0 ? Double(outputTokens) / evalSeconds : 0 }
    var promptTokensPerSecond: Double? { promptEvalSeconds > 0 ? Double(promptTokens) / promptEvalSeconds : nil }
}

struct SpeedSample: Identifiable {
    let id: Int
    let t: Double
    let tps: Double
}

/// Zustand und Messwerte einer Seite des Vergleichs.
@MainActor @Observable
final class ModelRun {
    enum Phase: Equatable {
        case idle, waiting, loading, thinking, streaming, done, cancelled
        case failed(String)
    }

    let side: Side

    var modelName: String {
        didSet { UserDefaults.standard.set(modelName, forKey: "model.\(side.rawValue)") }
    }

    private(set) var output = ""
    private(set) var thinking = ""
    private(set) var phase: Phase = .idle
    private(set) var liveTokens = 0
    private(set) var liveTPS: Double = 0
    private(set) var samples: [SpeedSample] = []
    private(set) var stats: GenerationStats?
    private(set) var startDate: Date?
    private(set) var firstTokenDate: Date?
    private(set) var endDate: Date?

    private(set) var sessionRuns = 0
    private(set) var sessionPromptTokens = 0
    private(set) var sessionOutputTokens = 0
    private(set) var sessionEvalSeconds = 0.0

    @ObservationIgnored private var recentTokens: [Date] = []
    @ObservationIgnored private var lastSampleDate: Date?

    init(side: Side) {
        self.side = side
        self.modelName = UserDefaults.standard.string(forKey: "model.\(side.rawValue)") ?? ""
    }

    var isActive: Bool { [.loading, .thinking, .streaming].contains(phase) }
    var displayTPS: Double { stats?.tokensPerSecond ?? liveTPS }
    var displayOutputTokens: Int { stats?.outputTokens ?? liveTokens }
    var timeToFirstToken: Double? {
        guard let startDate, let firstTokenDate else { return nil }
        return firstTokenDate.timeIntervalSince(startDate)
    }
    var sessionTotalTokens: Int { sessionPromptTokens + sessionOutputTokens }
    var sessionAverageTPS: Double { sessionEvalSeconds > 0 ? Double(sessionOutputTokens) / sessionEvalSeconds : 0 }

    func elapsed(at now: Date) -> Double? {
        guard let startDate else { return nil }
        return (endDate ?? now).timeIntervalSince(startDate)
    }

    func prepare(waiting: Bool) {
        output = ""
        thinking = ""
        stats = nil
        liveTokens = 0
        liveTPS = 0
        samples = []
        startDate = nil
        firstTokenDate = nil
        endDate = nil
        recentTokens = []
        lastSampleDate = nil
        phase = waiting ? .waiting : .loading
    }

    func markCancelled() {
        if phase == .waiting { phase = .cancelled }
    }

    func resetSession() {
        sessionRuns = 0
        sessionPromptTokens = 0
        sessionOutputTokens = 0
        sessionEvalSeconds = 0
    }

    func run(client: OllamaClient, prompt: String, options: GenerationOptions) async {
        prepare(waiting: false)
        startDate = Date()

        var messages: [ChatMessage] = []
        if !options.systemPrompt.isEmpty { messages.append(.init(role: "system", content: options.systemPrompt)) }
        messages.append(.init(role: "user", content: prompt))

        do {
            for try await timed in client.chat(model: modelName, messages: messages, options: options) {
                let chunk = timed.chunk
                let now = timed.receivedAt
                let piece = chunk.message?.content ?? ""
                let thought = chunk.message?.thinking ?? ""
                if !piece.isEmpty || !thought.isEmpty {
                    if firstTokenDate == nil { firstTokenDate = now }
                    liveTokens += 1
                    output += piece
                    thinking += thought
                    phase = piece.isEmpty ? .thinking : .streaming
                    recordSample(at: now)
                }
                if chunk.done == true { finish(with: chunk, at: now) }
            }
            if stats == nil {
                endDate = Date()
                phase = Task.isCancelled ? .cancelled : .failed("Der Stream wurde unerwartet beendet.")
            }
        } catch {
            endDate = Date()
            let cancelled = Task.isCancelled || error is CancellationError || (error as? URLError)?.code == .cancelled
            phase = cancelled ? .cancelled : .failed(error.localizedDescription)
        }
    }

    private func recordSample(at now: Date) {
        recentTokens.append(now)
        while let first = recentTokens.first, now.timeIntervalSince(first) > 1.0 { recentTokens.removeFirst() }
        guard let firstTokenDate else { return }

        let sinceFirst = now.timeIntervalSince(firstTokenDate)
        if sinceFirst > 0.05 { liveTPS = Double(liveTokens - 1) / sinceFirst }

        if lastSampleDate.map({ now.timeIntervalSince($0) >= 0.2 }) ?? true {
            // Gleitendes Fenster von einer Sekunde für die Kurve; die erste halbe Sekunde ist zu unruhig.
            let window = min(1.0, sinceFirst)
            if sinceFirst >= 0.5 {
                samples.append(SpeedSample(id: samples.count, t: sinceFirst, tps: Double(recentTokens.count) / window))
                lastSampleDate = now
            }
        }
    }

    private func finish(with chunk: ChatChunk, at now: Date) {
        func seconds(_ ns: Int64?) -> Double { Double(ns ?? 0) / 1_000_000_000 }
        let result = GenerationStats(
            promptTokens: chunk.prompt_eval_count ?? 0,
            outputTokens: chunk.eval_count ?? liveTokens,
            promptEvalSeconds: seconds(chunk.prompt_eval_duration),
            evalSeconds: seconds(chunk.eval_duration),
            loadSeconds: seconds(chunk.load_duration),
            totalSeconds: seconds(chunk.total_duration),
            timeToFirstToken: timeToFirstToken
        )
        stats = result
        endDate = now
        phase = .done
        sessionRuns += 1
        sessionPromptTokens += result.promptTokens
        sessionOutputTokens += result.outputTokens
        sessionEvalSeconds += result.evalSeconds
    }
}
