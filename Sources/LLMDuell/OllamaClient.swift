import Foundation

struct OllamaModel: Identifiable, Hashable, Decodable {
    struct Details: Hashable, Decodable {
        let family: String?
        let parameter_size: String?
        let quantization_level: String?
    }

    let name: String
    let size: Int64
    let details: Details?

    var id: String { name }
    var sizeLabel: String { ByteCountFormatter.string(fromByteCount: size, countStyle: .file) }
}

struct ChatMessage: Encodable {
    let role: String
    let content: String
}

/// Eine Zeile aus dem NDJSON-Stream von `/api/chat`.
struct ChatChunk: Decodable {
    struct Message: Decodable {
        let content: String?
        let thinking: String?
    }

    let message: Message?
    let done: Bool?
    let error: String?
    let total_duration: Int64?
    let load_duration: Int64?
    let prompt_eval_count: Int?
    let prompt_eval_duration: Int64?
    let eval_count: Int?
    let eval_duration: Int64?
}

/// Chunk mit dem Empfangszeitpunkt, gemessen abseits des UI-Threads.
struct TimedChunk {
    let chunk: ChatChunk
    let receivedAt: Date
}

struct GenerationOptions {
    var temperature: Double
    var seed: Int?
    var systemPrompt: String
}

enum OllamaError: LocalizedError {
    case server(String)
    case http(Int)

    var errorDescription: String? {
        switch self {
        case .server(let message): return message
        case .http(let code): return "Ollama antwortete mit HTTP \(code)"
        }
    }
}

struct OllamaClient {
    let baseURL: URL

    func version() async throws -> String {
        struct Response: Decodable { let version: String }
        let (data, response) = try await URLSession.shared.data(from: baseURL.appending(path: "api/version"))
        try Self.check(response, data)
        return try JSONDecoder().decode(Response.self, from: data).version
    }

    func listModels() async throws -> [OllamaModel] {
        struct Response: Decodable { let models: [OllamaModel] }
        let (data, response) = try await URLSession.shared.data(from: baseURL.appending(path: "api/tags"))
        try Self.check(response, data)
        return try JSONDecoder().decode(Response.self, from: data).models
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    func chat(model: String, messages: [ChatMessage], options: GenerationOptions) -> AsyncThrowingStream<TimedChunk, Error> {
        AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    var request = URLRequest(url: baseURL.appending(path: "api/chat"))
                    request.httpMethod = "POST"
                    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                    // Großzügig, weil das erste Laden eines großen Modells dauern kann.
                    request.timeoutInterval = 900

                    var opts: [String: Any] = ["temperature": options.temperature]
                    if let seed = options.seed { opts["seed"] = seed }
                    let body: [String: Any] = [
                        "model": model,
                        "messages": messages.map { ["role": $0.role, "content": $0.content] },
                        "stream": true,
                        "options": opts,
                    ]
                    request.httpBody = try JSONSerialization.data(withJSONObject: body)

                    let (bytes, response) = try await URLSession.shared.bytes(for: request)
                    let decoder = JSONDecoder()

                    if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                        var text = ""
                        for try await line in bytes.lines { text += line }
                        if let chunk = try? decoder.decode(ChatChunk.self, from: Data(text.utf8)), let error = chunk.error {
                            throw OllamaError.server(error)
                        }
                        throw OllamaError.http(http.statusCode)
                    }

                    for try await line in bytes.lines where !line.isEmpty {
                        let receivedAt = Date()
                        let chunk = try decoder.decode(ChatChunk.self, from: Data(line.utf8))
                        if let error = chunk.error { throw OllamaError.server(error) }
                        continuation.yield(TimedChunk(chunk: chunk, receivedAt: receivedAt))
                        if chunk.done == true { break }
                    }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    private static func check(_ response: URLResponse, _ data: Data) throws {
        guard let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) else { return }
        if let chunk = try? JSONDecoder().decode(ChatChunk.self, from: data), let error = chunk.error {
            throw OllamaError.server(error)
        }
        throw OllamaError.http(http.statusCode)
    }
}
