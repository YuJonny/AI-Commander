import Foundation

@main
struct PipelineDiagnosticsTests {
    static func main() throws {
        guard PipelineDiagnostics.isEnabled else {
            fatalError("Set AI_OPERATOR_TRACE=1 for this test")
        }
        let diagnostics = PipelineDiagnostics.shared
        guard let sessionID = diagnostics.begin(
            model: "qwen",
            modelDirectory: "/tmp/qwen-model",
            cleanFillers: true,
            useLLM: true,
            targetAppName: "DiagnosticsTest",
            targetAppBundleID: "com.example.test") else {
            fatalError("Failed to create diagnostics session")
        }

        diagnostics.recordAudio(
            sessionID,
            samples: [0, 0.1, -0.1, 0.25, -0.25],
            sampleRate: 16_000,
            nativeSampleRate: 48_000,
            nativeSampleCount: 15,
            channelCount: 1)
        diagnostics.record(sessionID, stage: "asr.joined_raw", text: "chat gpt")
        diagnostics.record(sessionID, stage: "terms.normalized", text: "ChatGPT")
        diagnostics.finish(sessionID, outcome: "test_complete", finalText: "ChatGPT")

        let directory = PipelineDiagnostics.rootURL.appendingPathComponent(sessionID, isDirectory: true)
        let traceData = try Data(contentsOf: directory.appendingPathComponent("trace.json"))
        let trace = try JSONDecoder().decode(PipelineDiagnostics.Trace.self, from: traceData)
        let names = trace.stages.map(\.name)
        guard trace.outcome == "test_complete",
              names.contains("audio.captured_16k"),
              names.contains("asr.joined_raw"),
              names.contains("terms.normalized"),
              names.last == "session.finished" else {
            fatalError("Trace stages or outcome are incomplete: \(names)")
        }

        let wav = try Data(contentsOf: directory.appendingPathComponent("input.wav"))
        guard wav.count >= 44,
              String(data: wav.prefix(4), encoding: .ascii) == "RIFF",
              String(data: wav[8..<12], encoding: .ascii) == "WAVE" else {
            fatalError("WAV header is invalid")
        }
        print("PipelineDiagnostics: \(trace.stages.count) stages passed at \(directory.path)")
    }
}
