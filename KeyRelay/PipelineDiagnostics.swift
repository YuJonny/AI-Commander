import Foundation

/// 开发期语音管线诊断。默认关闭；显式开启后，每次口述保存 16k WAV 和逐阶段 trace.json。
/// 正式功能不读取这些文件，也不会上传；最多保留最近 50 次会话。
final class PipelineDiagnostics {
    static let shared = PipelineDiagnostics()
    static let defaultsKey = "pipelineDiagnosticsEnabled"
    static let maxSessions = 50

    struct Settings: Codable {
        let cleanFillers: Bool
        let useLLM: Bool
    }

    struct Stage: Codable {
        let name: String
        let atMs: Double
        let durationMs: Double?
        let text: String?
        let textCharacters: Int?
        let metrics: [String: Double]?
        let details: [String: String]?
    }

    struct Trace: Codable {
        let schemaVersion: Int
        let sessionID: String
        let startedAt: String
        var completedAt: String?
        let appVersion: String
        let model: String
        let modelDirectory: String
        let settings: Settings
        let targetAppName: String?
        let targetAppBundleID: String?
        var outcome: String?
        var stages: [Stage]
    }

    private struct ActiveSession {
        var trace: Trace
        let directory: URL
        let started: Date
    }

    private let lock = NSLock()
    private var sessions: [String: ActiveSession] = [:]
    private let encoder: JSONEncoder = {
        let value = JSONEncoder()
        value.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return value
    }()
    private let isoFormatter: ISO8601DateFormatter = {
        let value = ISO8601DateFormatter()
        value.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return value
    }()
    private let folderFormatter: DateFormatter = {
        let value = DateFormatter()
        value.locale = Locale(identifier: "en_US_POSIX")
        value.dateFormat = "yyyyMMdd-HHmmss-SSS"
        return value
    }()

    private init() {}

    static var isEnabled: Bool {
        let environment = ProcessInfo.processInfo.environment["AI_OPERATOR_TRACE"]?.lowercased()
        if ["1", "true", "yes"].contains(environment) { return true }
        return UserDefaults.standard.bool(forKey: defaultsKey)
    }

    static var rootURL: URL {
        if let custom = ProcessInfo.processInfo.environment["AI_OPERATOR_TRACE_DIR"], !custom.isEmpty {
            return URL(fileURLWithPath: custom, isDirectory: true)
        }
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("AI接线员/diagnostics", isDirectory: true)
    }

    @discardableResult
    func begin(
        model: String,
        modelDirectory: String,
        cleanFillers: Bool,
        useLLM: Bool,
        targetAppName: String?,
        targetAppBundleID: String?
    ) -> String? {
        guard Self.isEnabled else { return nil }
        lock.lock()
        defer { lock.unlock() }

        let now = Date()
        let sessionID = "\(folderFormatter.string(from: now))-\(UUID().uuidString.prefix(8))"
        let directory = Self.rootURL.appendingPathComponent(sessionID, isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            pruneSessionsLocked(excluding: directory)
        } catch {
            NSLog("[PipelineDiagnostics] cannot create session directory: \(error)")
            return nil
        }

        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
        let initialStage = Stage(
            name: "session.started",
            atMs: 0,
            durationMs: nil,
            text: nil,
            textCharacters: nil,
            metrics: nil,
            details: nil)
        let trace = Trace(
            schemaVersion: 1,
            sessionID: sessionID,
            startedAt: isoFormatter.string(from: now),
            completedAt: nil,
            appVersion: version,
            model: model,
            modelDirectory: URL(fileURLWithPath: modelDirectory).lastPathComponent,
            settings: Settings(cleanFillers: cleanFillers, useLLM: useLLM),
            targetAppName: targetAppName,
            targetAppBundleID: targetAppBundleID,
            outcome: nil,
            stages: [initialStage])
        let session = ActiveSession(trace: trace, directory: directory, started: now)
        sessions[sessionID] = session
        persistLocked(session)
        NSLog("[PipelineDiagnostics] session=\(sessionID) directory=\(directory.path)")
        return sessionID
    }

    func record(
        _ sessionID: String?,
        stage name: String,
        text: String? = nil,
        durationMs: Double? = nil,
        metrics: [String: Double]? = nil,
        details: [String: String]? = nil
    ) {
        guard let sessionID else { return }
        lock.lock()
        defer { lock.unlock() }
        guard var session = sessions[sessionID] else { return }
        session.trace.stages.append(Stage(
            name: name,
            atMs: Date().timeIntervalSince(session.started) * 1000,
            durationMs: durationMs,
            text: text,
            textCharacters: text?.count,
            metrics: metrics,
            details: details))
        sessions[sessionID] = session
        persistLocked(session)
    }

    func recordAudio(
        _ sessionID: String?,
        samples: [Float],
        sampleRate: Int,
        nativeSampleRate: Double,
        nativeSampleCount: Int,
        channelCount: Int
    ) {
        guard let sessionID else { return }
        lock.lock()
        guard let directory = sessions[sessionID]?.directory else {
            lock.unlock()
            return
        }
        lock.unlock()

        let audioURL = directory.appendingPathComponent("input.wav")
        var saved = false
        do {
            try Self.writePCM16Wav(samples: samples, sampleRate: sampleRate, to: audioURL)
            saved = true
        } catch {
            NSLog("[PipelineDiagnostics] cannot save audio: \(error)")
        }

        var peak: Float = 0
        var sum: Double = 0
        var sumSquares: Double = 0
        var nearZero = 0
        var clipped = 0
        for value in samples {
            let absolute = abs(value)
            peak = max(peak, absolute)
            sum += Double(value)
            sumSquares += Double(value * value)
            if absolute < 0.000_001 { nearZero += 1 }
            if absolute >= 0.999 { clipped += 1 }
        }
        let count = max(samples.count, 1)
        let rms = (sumSquares / Double(count)).squareRoot()
        record(
            sessionID,
            stage: "audio.captured_16k",
            metrics: [
                "sampleRate": Double(sampleRate),
                "sampleCount": Double(samples.count),
                "durationSeconds": Double(samples.count) / Double(sampleRate),
                "nativeSampleRate": nativeSampleRate,
                "nativeSampleCount": Double(nativeSampleCount),
                "nativeDurationSeconds": nativeSampleRate > 0 ? Double(nativeSampleCount) / nativeSampleRate : 0,
                "channelCount": Double(channelCount),
                "peak": Double(peak),
                "rms": rms,
                "dcOffset": sum / Double(count),
                "nearZeroRatio": Double(nearZero) / Double(count),
                "clippedRatio": Double(clipped) / Double(count),
            ],
            details: ["audioFile": saved ? "input.wav" : "write_failed"])
    }

    func finish(
        _ sessionID: String?,
        outcome: String,
        finalText: String? = nil,
        details: [String: String]? = nil
    ) {
        guard let sessionID else { return }
        lock.lock()
        defer { lock.unlock() }
        guard var session = sessions[sessionID] else { return }
        let now = Date()
        session.trace.outcome = outcome
        session.trace.completedAt = isoFormatter.string(from: now)
        session.trace.stages.append(Stage(
            name: "session.finished",
            atMs: now.timeIntervalSince(session.started) * 1000,
            durationMs: now.timeIntervalSince(session.started) * 1000,
            text: finalText,
            textCharacters: finalText?.count,
            metrics: nil,
            details: details))
        persistLocked(session)
        sessions.removeValue(forKey: sessionID)
    }

    private func persistLocked(_ session: ActiveSession) {
        do {
            let data = try encoder.encode(session.trace)
            try data.write(to: session.directory.appendingPathComponent("trace.json"), options: .atomic)
        } catch {
            NSLog("[PipelineDiagnostics] cannot persist trace: \(error)")
        }
    }

    private func pruneSessionsLocked(excluding current: URL) {
        let fileManager = FileManager.default
        guard let urls = try? fileManager.contentsOfDirectory(
            at: Self.rootURL,
            includingPropertiesForKeys: [.contentModificationDateKey, .isDirectoryKey],
            options: [.skipsHiddenFiles]) else { return }
        let directories = urls.filter { url in
            guard url != current else { return false }
            return (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true
        }.sorted { lhs, rhs in
            let left = (try? lhs.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            let right = (try? rhs.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            return left > right
        }
        for url in directories.dropFirst(max(Self.maxSessions - 1, 0)) {
            try? fileManager.removeItem(at: url)
        }
    }

    private static func writePCM16Wav(samples: [Float], sampleRate: Int, to url: URL) throws {
        let bytesPerSample = 2
        let dataSize = samples.count * bytesPerSample
        var data = Data()
        data.appendASCII("RIFF")
        data.appendLittleEndian(UInt32(36 + dataSize))
        data.appendASCII("WAVE")
        data.appendASCII("fmt ")
        data.appendLittleEndian(UInt32(16))
        data.appendLittleEndian(UInt16(1))
        data.appendLittleEndian(UInt16(1))
        data.appendLittleEndian(UInt32(sampleRate))
        data.appendLittleEndian(UInt32(sampleRate * bytesPerSample))
        data.appendLittleEndian(UInt16(bytesPerSample))
        data.appendLittleEndian(UInt16(16))
        data.appendASCII("data")
        data.appendLittleEndian(UInt32(dataSize))
        for sample in samples {
            let clamped = max(-1, min(1, sample))
            data.appendLittleEndian(Int16(clamped * 32_767))
        }
        try data.write(to: url, options: .atomic)
    }
}

private extension Data {
    mutating func appendASCII(_ string: String) {
        if let value = string.data(using: .ascii) { append(value) }
    }

    mutating func appendLittleEndian<T: FixedWidthInteger>(_ value: T) {
        var littleEndian = value.littleEndian
        Swift.withUnsafeBytes(of: &littleEndian) { append(contentsOf: $0) }
    }
}
