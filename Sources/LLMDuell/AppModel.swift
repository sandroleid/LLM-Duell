import Foundation
import Observation

enum RunMode: String, CaseIterable, Identifiable {
    case sequential = "Nacheinander"
    case parallel = "Parallel"

    var id: Self { self }
}

enum ConnectionState: Equatable {
    case checking
    case connected(version: String)
    case failed(String)
}

@MainActor @Observable
final class AppModel {
    private let defaults = UserDefaults.standard

    var host: String { didSet { defaults.set(host, forKey: "host") } }
    var mode: RunMode { didSet { defaults.set(mode.rawValue, forKey: "mode") } }
    var temperature: Double { didSet { defaults.set(temperature, forKey: "temperature") } }
    var useSeed: Bool { didSet { defaults.set(useSeed, forKey: "useSeed") } }
    var seed: Int { didSet { defaults.set(seed, forKey: "seed") } }
    var systemPrompt: String { didSet { defaults.set(systemPrompt, forKey: "systemPrompt") } }

    var prompt = ""
    private(set) var models: [OllamaModel] = []
    private(set) var connection: ConnectionState = .checking
    private(set) var runTask: Task<Void, Never>?

    let left = ModelRun(side: .left)
    let right = ModelRun(side: .right)

    init() {
        host = defaults.string(forKey: "host") ?? "http://localhost:11434"
        mode = defaults.string(forKey: "mode").flatMap(RunMode.init(rawValue:)) ?? .sequential
        temperature = defaults.object(forKey: "temperature") as? Double ?? 0.7
        useSeed = defaults.object(forKey: "useSeed") as? Bool ?? true
        seed = defaults.object(forKey: "seed") as? Int ?? 42
        systemPrompt = defaults.string(forKey: "systemPrompt") ?? ""
    }

    var isRunning: Bool { runTask != nil }

    var client: OllamaClient? {
        var value = host.trimmingCharacters(in: .whitespacesAndNewlines)
        if !value.contains("://") { value = "http://" + value }
        while value.hasSuffix("/") { value.removeLast() }
        return URL(string: value).map(OllamaClient.init)
    }

    var canStart: Bool {
        !isRunning && client != nil
            && !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !left.modelName.isEmpty && !right.modelName.isEmpty
    }

    func model(named name: String) -> OllamaModel? {
        models.first { $0.name == name }
    }

    func refreshModels() async {
        connection = .checking
        guard let client else {
            connection = .failed("Ungültige Host-Adresse")
            return
        }
        do {
            async let version = client.version()
            models = try await client.listModels()
            connection = .connected(version: (try? await version) ?? "")
            if model(named: left.modelName) == nil { left.modelName = models.first?.name ?? "" }
            if model(named: right.modelName) == nil {
                right.modelName = models.first { $0.name != left.modelName }?.name ?? left.modelName
            }
        } catch {
            models = []
            connection = .failed(error.localizedDescription)
        }
    }

    func start() {
        guard canStart, let client else { return }
        let prompt = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        let options = GenerationOptions(
            temperature: temperature,
            seed: useSeed ? seed : nil,
            systemPrompt: systemPrompt.trimmingCharacters(in: .whitespacesAndNewlines)
        )
        let mode = mode
        left.prepare(waiting: false)
        right.prepare(waiting: mode == .sequential)

        runTask = Task {
            switch mode {
            case .sequential:
                await left.run(client: client, prompt: prompt, options: options)
                if Task.isCancelled {
                    right.markCancelled()
                } else {
                    await right.run(client: client, prompt: prompt, options: options)
                }
            case .parallel:
                async let a: Void = left.run(client: client, prompt: prompt, options: options)
                async let b: Void = right.run(client: client, prompt: prompt, options: options)
                _ = await (a, b)
            }
            runTask = nil
        }
    }

    func stop() {
        runTask?.cancel()
    }

    func swapModels() {
        guard !isRunning else { return }
        (left.modelName, right.modelName) = (right.modelName, left.modelName)
    }

    func resetSession() {
        left.resetSession()
        right.resetSession()
    }
}
