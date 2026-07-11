import Foundation
import AppKit
import AVFoundation
import Carbon.HIToolbox

private func L(_ key: String) -> String {
    guard let lang = UserDefaults.standard.string(forKey: "appLanguage") else {
        return NSLocalizedString(key, comment: "")
    }
    guard let path = Bundle.main.path(forResource: lang, ofType: "lproj"),
          let bundle = Bundle(path: path) else {
        return NSLocalizedString(key, comment: "")
    }
    return bundle.localizedString(forKey: key, value: nil, table: nil)
}

/// v2.0 语音输入控制器：按耳机键 → 录音 → 转写(规则清理) → 键盘合成输入到当前 App。
/// 全程本地；不触碰剪贴板。状态通过 onState 驱动悬浮窗与窗口状态。
final class VoiceController {
    enum ASRModel: String {
        case qwen
        case senseVoice
    }

    enum State {
        case listening, transcribing, enhancing, inserted(String), noAudio, noTranscript, error(String)
        var icon: String {
            switch self {
            case .listening:    return "🎙"
            case .transcribing: return "✍️"
            case .enhancing:    return "✨"
            case .inserted:     return "✅"
            case .noAudio:      return "🎙"
            case .noTranscript: return "🤔"
            case .error:        return "⚠️"
            }
        }
        var text: String {
            switch self {
            case .listening:        return L("state.listening")
            case .transcribing:     return L("state.transcribing")
            case .enhancing:        return L("state.enhancing")
            case .inserted(let t):  return t
            case .noAudio:          return L("state.noaudio")
            case .noTranscript:     return L("state.notranscript")
            case .error(let e):     return e
            }
        }
    }

    private let recorder = AudioRecorder()
    private var engine: VoiceEngine?
    private var engineModelDir: String?
    private let queue = DispatchQueue(label: "com.vvclaw.commander.voice")
    private var engineRelease: DispatchWorkItem?          // 闲时卸载模型
    private let engineIdleSeconds: TimeInterval = 300     // 5 分钟不用就释放，内存回落
    /// LLM 润色输入上限（字符）：约 3~4 分钟连续口述。超过则跳过润色只走规则，
    /// 防止超出 llama-server 4096 token 上下文后截断吐半截。
    static let llmInputMaxCharacters = 1000

    // 伪流式：录音中 VAD 每闭合一段（停顿 ≥1.5s）就后台先转写，按停只剩尾段。
    // 全部状态只在 `queue` 上读写。
    private var streaming: VoiceEngine.Streaming?
    private var streamingUnavailable = false   // 本次录音已确认不可用（如 SenseVoice 档）就不再反复尝试
    private var streamTimer: DispatchSourceTimer?

    /// 按键防抖：窗口内的后续按键忽略（连按两次=一次）。
    /// 治两件事：停止后手抖多按一下开出"幽灵录音"；线控按键的电气抖动。
    private let toggleDebounceSeconds: TimeInterval = 0.5
    private var lastToggleAt = Date.distantPast

    /// 录音时长上限：到点自动"按停"，照常转写插入，一个字不丢
    ///（超 1000 字润色自动跳过，走规则清理；更长场景本产品不承诺）。
    static let maxRecordingSeconds: TimeInterval = 1200   // 20 分钟
    private var autoStopWork: DispatchWorkItem?

    var onState: ((State) -> Void)?
    private(set) var isRecording = false
    private var selectedModel: ASRModel = .qwen
    private var recordingModel: ASRModel?
    private var recordingModelDir: String?
    private var recordingTraceID: String?
    private var recordingCleanFillers = true
    private var recordingUseLLM = false

    var cleanFillers: Bool = true   // 口语词优化（规则去口水词）
    var useLLM: Bool = false        // 本地 LLM 增强（内嵌 llama.cpp + Qwen3-8B）
    var isVoiceModelDownloading = false

    func selectModel(_ model: ASRModel) {
        guard selectedModel != model else { return }
        selectedModel = model
        invalidateEngine()
    }

    func invalidateEngine() {
        queue.async { [weak self] in
            guard let self else { return }
            self.engineRelease?.cancel()
            self.engine = nil
            self.engineModelDir = nil
        }
    }

    func toggle() {
        let now = Date()
        guard now.timeIntervalSince(lastToggleAt) >= toggleDebounceSeconds else { return }
        lastToggleAt = now
        if isRecording { stopAndTranscribe() } else { startRecording() }
    }

    /// 语音模式启用时调用（此时 App 在前台）：若权限未决定就弹一次系统授权框。
    /// 这样到按耳机键时通常已是 authorized，直接录音、不在后台弹框。
    func ensureMicPermission() {
        guard AVCaptureDevice.authorizationStatus(for: .audio) == .notDetermined else { return }
        NSApp.activate(ignoringOtherApps: true)   // 必须在前台，系统才肯弹授权框
        AVCaptureDevice.requestAccess(for: .audio) { _ in }
    }

    private func startRecording() {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized:
            guard let modelDir = VoiceController.resolveModelDir(for: selectedModel) else {
                // 模型还没就位：指引到界面上的下载按钮，不擅自开下
                onState?(.error(isVoiceModelDownloading
                                ? L("err.model.downloading")
                                : L("err.model.first")))
                return
            }
            beginCapture(model: selectedModel, modelDir: modelDir)
        case .notDetermined:
            onState?(.error(L("err.mic.requesting")))
            AVCaptureDevice.requestAccess(for: .audio) { [weak self] granted in
                DispatchQueue.main.async {
                    guard let self else { return }
                    guard granted else {
                        self.onState?(.error(L("err.mic.denied")))
                        return
                    }
                    if let modelDir = VoiceController.resolveModelDir(for: self.selectedModel) {
                        self.beginCapture(model: self.selectedModel, modelDir: modelDir)
                    } else {
                        self.onState?(.error(self.isVoiceModelDownloading
                                             ? L("err.model.downloading")
                                             : L("err.model.first")))
                    }
                }
            }
        case .denied, .restricted:
            onState?(.error(L("err.mic.settings")))
        @unknown default:
            onState?(.error(L("err.mic.unknown")))
        }
    }

    private func beginCapture(model: ASRModel, modelDir: String) {
        do {
            try recorder.start()
            isRecording = true
            recordingModel = model
            recordingModelDir = modelDir
            recordingCleanFillers = cleanFillers
            recordingUseLLM = useLLM
            let targetApp = NSWorkspace.shared.frontmostApplication
            let traceID = PipelineDiagnostics.shared.begin(
                model: model.rawValue,
                modelDirectory: modelDir,
                cleanFillers: recordingCleanFillers,
                useLLM: recordingUseLLM,
                targetAppName: targetApp?.localizedName,
                targetAppBundleID: targetApp?.bundleIdentifier)
            recordingTraceID = traceID
            PipelineDiagnostics.shared.record(
                traceID,
                stage: "audio.capture_started",
                details: ["selectedModel": model.rawValue])
            onState?(.listening)
            preloadEngine(
                modelDir: modelDir,
                useLLM: recordingUseLLM,
                diagnosticsSessionID: traceID)   // 利用说话的间隙预加载模型
            startStreamTimer(modelDir: modelDir, traceID: traceID)
            scheduleAutoStop(traceID: traceID)
        } catch {
            onState?(.error(String(format: L("err.mic.start"), error.localizedDescription)))
        }
    }

    /// 20 分钟到点自动停止：走与手动按停完全相同的转写→润色→插入流程
    private func scheduleAutoStop(traceID: String?) {
        autoStopWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.isRecording else { return }
            NSLog("[VoiceController] auto-stop: reached max recording duration")
            PipelineDiagnostics.shared.record(
                traceID,
                stage: "audio.auto_stop_limit",
                metrics: ["limitSeconds": VoiceController.maxRecordingSeconds])
            self.stopAndTranscribe()
        }
        autoStopWork = work
        DispatchQueue.main.asyncAfter(
            deadline: .now() + VoiceController.maxRecordingSeconds, execute: work)
    }

    /// 伪流式驱动：每 0.4s 在语音队列上增量喂音（与预加载/停止同队列，天然互斥）。
    /// 引擎未就绪或非 Qwen 档时安静跳过；停止时未转写的部分照常走尾段/批式。
    private func startStreamTimer(modelDir: String, traceID: String?) {
        streamTimer?.cancel()
        queue.async { [weak self] in   // 防御性清零：上次录音若异常中断，不让旧会话漏进本次
            self?.streaming = nil
            self?.streamingUnavailable = false
        }
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 0.5, repeating: 0.4)
        timer.setEventHandler { [weak self] in
            guard let self, self.recorder.isRecording else { return }
            if self.streaming == nil, !self.streamingUnavailable {
                guard let engine = self.engine, self.engineModelDir == modelDir else { return }
                if let session = VoiceEngine.Streaming(
                    engine: engine,
                    nativeRate: self.recorder.nativeRate,
                    traceID: traceID) {
                    self.streaming = session
                } else {
                    self.streamingUnavailable = true   // SenseVoice / 无 VAD：本次录音不再尝试
                    return
                }
            }
            guard let session = self.streaming else { return }
            let chunk = self.recorder.nativeSamples(after: session.nativeConsumed)
            if !chunk.isEmpty { session.accept(native: chunk) }
        }
        timer.resume()
        streamTimer = timer
    }

    /// 在录音开始时就把引擎加载好；与转写共用同一串行队列，
    /// 停止时的转写会自然排在加载之后，无需额外同步。
    private func preloadEngine(
        modelDir: String,
        useLLM: Bool,
        diagnosticsSessionID: String?
    ) {
        let diagnostics = PipelineDiagnostics.shared
        if useLLM {
            diagnostics.record(diagnosticsSessionID, stage: "llm.warmup_requested")
            VoiceEngine.llmWarmup()   // 7b 也趁说话间隙预热
        }
        queue.async { [weak self] in
            guard let self else { return }
            let started = Date()
            self.engineRelease?.cancel()
            if self.engineModelDir != modelDir {
                self.engine = nil
                self.engineModelDir = nil
            }
            let reused = self.engine != nil
            if self.engine == nil {
                self.engine = VoiceEngine(modelDir: modelDir)
                self.engineModelDir = modelDir
            }
            diagnostics.record(
                diagnosticsSessionID,
                stage: "engine.preload_ready",
                durationMs: Date().timeIntervalSince(started) * 1000,
                details: [
                    "cached": reused ? "true" : "false",
                    "modelDirectory": URL(fileURLWithPath: modelDir).lastPathComponent,
                ])
        }
    }

    private func stopAndTranscribe() {
        autoStopWork?.cancel()
        autoStopWork = nil
        streamTimer?.cancel()
        streamTimer = nil
        let capture = recorder.stop()
        let samples = capture.samples
        let model = recordingModel
        let modelDir = recordingModelDir
        let traceID = recordingTraceID
        let cleanFillers = recordingCleanFillers
        let useLLM = recordingUseLLM
        recordingModel = nil
        recordingModelDir = nil
        recordingTraceID = nil
        isRecording = false
        onState?(.transcribing)
        queue.async { [weak self] in
            guard let self else { return }
            let diagnostics = PipelineDiagnostics.shared
            self.engineRelease?.cancel()                 // 正在用，取消待卸载
            // 伪流式会话必须在所有提前返回之前收割并清零，否则残留状态会污染下一次录音
            let session = self.streaming
            self.streaming = nil
            self.streamingUnavailable = false
            diagnostics.recordAudio(
                traceID,
                samples: samples,
                sampleRate: capture.sampleRate,
                nativeSampleRate: capture.nativeSampleRate,
                nativeSampleCount: capture.nativeSampleCount,
                channelCount: capture.channelCount)
            // 静音检测：误触/没说话就丢弃，不去请模型、更不插入乱码
            var peak: Float = 0, sumsq: Float = 0
            for v in samples { let a = abs(v); if a > peak { peak = a }; sumsq += v * v }
            let rms = samples.isEmpty ? 0 : (sumsq / Float(samples.count)).squareRoot()
            let duration = Double(samples.count) / Double(capture.sampleRate)
            let metrics = String(format: "samples=%d duration=%.2fs peak=%.5f rms=%.5f",
                                 samples.count, duration, peak, rms)
            NSLog("[VoiceController] capture model=\(model?.rawValue ?? "unknown") \(metrics)")
            // 这里只拦截近乎没有信号的录音；较轻的人声交给 ASR 判断，避免误报“没听到”。
            let signalRejected = samples.count < 1_600 || (peak < 0.008 && rms < 0.0015)
            diagnostics.record(
                traceID,
                stage: "audio.signal_gate",
                metrics: [
                    "samples": Double(samples.count),
                    "durationSeconds": duration,
                    "peak": Double(peak),
                    "rms": Double(rms),
                    "minimumSamples": 1_600,
                    "peakThreshold": 0.008,
                    "rmsThreshold": 0.0015,
                    "rejected": signalRejected ? 1 : 0,
                ])
            if signalRejected {
                self.scheduleEngineRelease()
                diagnostics.finish(traceID, outcome: "no_audio")
                self.onState?(.noAudio)
                return
            }
            guard let dir = modelDir else {
                self.scheduleEngineRelease()
                diagnostics.finish(traceID, outcome: "model_missing")
                self.onState?(.error(L("err.model.missing")))
                return
            }
            let engineStarted = Date()
            if self.engineModelDir != dir {
                self.engine = nil
                self.engineModelDir = nil
            }
            let reusedEngine = self.engine != nil
            if self.engine == nil {
                self.engine = VoiceEngine(modelDir: dir)
                self.engineModelDir = dir
            }
            diagnostics.record(
                traceID,
                stage: "engine.decode_ready",
                durationMs: Date().timeIntervalSince(engineStarted) * 1000,
                details: ["cached": reusedEngine ? "true" : "false"])
            // 伪流式收尾：录音中已转写的段直接用，只补尾段；
            // 引擎中途变过（换模型/卸载）则丢弃流式结果，整段批式重转，保证正确性。
            let base: String
            if let session, session.matches(engine: self.engine) {
                let tail = self.recorder.nativeSamples(after: session.nativeConsumed)
                let parts = session.finish(nativeTail: tail)
                base = self.engine!.finalizeParts(
                    parts,
                    segmentCount: parts.count,
                    diagnosticsSessionID: traceID)
            } else {
                base = self.engine!.transcribeRaw(
                    samples: samples,
                    sampleRate: capture.sampleRate,
                    diagnosticsSessionID: traceID)
            }
            NSLog("[VoiceController] asr model=\(model?.rawValue ?? "unknown") characters=\(base.count)")
            if base.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                self.scheduleEngineRelease()
                diagnostics.finish(traceID, outcome: "no_transcript")
                self.onState?(.noTranscript)
                return
            }
            diagnostics.record(
                traceID,
                stage: "pipeline.route",
                text: base,
                details: [
                    "cleanFillers": cleanFillers ? "true" : "false",
                    "useLLM": useLLM ? "true" : "false",
                ])
            var text: String
            if useLLM, !base.isEmpty {
                // 分工：口水词由规则先删（确定、不误伤）；LLM 只做口误更正/去重/结构化。
                // LLM 失败(如 ollama 没开)则回退常规管线。
                let pre: String
                if cleanFillers {
                    let rulesStarted = Date()
                    pre = Rules.clean(base) { step, value in
                        diagnostics.record(traceID, stage: "rules.\(step)", text: value)
                    }
                    diagnostics.record(
                        traceID,
                        stage: "rules.completed",
                        text: pre,
                        durationMs: Date().timeIntervalSince(rulesStarted) * 1000)
                } else {
                    pre = base
                    diagnostics.record(traceID, stage: "rules.bypassed", text: pre)
                }
                if pre.count > VoiceController.llmInputMaxCharacters {
                    // 超长保险：llama-server 上下文 4096 token ≈ prompt(~800) + 输入 + 等长输出。
                    // 超过阈值润色必然截断/溢出，自动降级为规则清理，防止吐半截。
                    diagnostics.record(
                        traceID,
                        stage: "llm.skipped_input_too_long",
                        metrics: [
                            "characters": Double(pre.count),
                            "threshold": Double(VoiceController.llmInputMaxCharacters),
                        ],
                        details: ["fallback": "regular_pipeline"])
                    NSLog("[VoiceController] llm skipped: input=\(pre.count) chars > \(VoiceController.llmInputMaxCharacters)")
                    text = self.engine!.polish(
                        base,
                        clean: cleanFillers,
                        diagnosticsSessionID: traceID,
                        stagePrefix: "fallback")
                } else {
                    self.onState?(.enhancing)
                    diagnostics.record(
                        traceID,
                        stage: "punctuation.delegated_to_llm",
                        text: pre)
                    if let enhanced = VoiceEngine.llmEnhance(
                        pre,
                        diagnosticsSessionID: traceID) {
                        text = enhanced
                        diagnostics.record(traceID, stage: "pipeline.llm_selected", text: text)
                    } else {
                        diagnostics.record(
                            traceID,
                            stage: "pipeline.llm_fallback",
                            text: base,
                            details: ["fallback": "regular_pipeline"])
                        text = self.engine!.polish(
                            base,
                            clean: cleanFillers,
                            diagnosticsSessionID: traceID,
                            stagePrefix: "fallback")
                    }
                }
            } else {
                text = self.engine!.polish(
                    base,
                    clean: cleanFillers,
                    diagnosticsSessionID: traceID)
            }
            text = text.trimmingCharacters(in: .whitespacesAndNewlines)
            diagnostics.record(traceID, stage: "pipeline.final_text", text: text)
            self.scheduleEngineRelease()
            if text.isEmpty {
                diagnostics.finish(traceID, outcome: "empty_final_text")
                self.onState?(.noTranscript)
                return
            }
            // 增强模式下状态框附带原文，便于对照"ASR 听到什么 vs LLM 改了什么"
            let display = (useLLM && text != base && !base.isEmpty)
                ? text + "\n〔原文〕" + base : text
            DispatchQueue.main.async {
                // 安全输入区（密码框等）系统会拦截合成键入——明确提示而不是静默失败
                if IsSecureEventInputEnabled() {
                    diagnostics.record(
                        traceID,
                        stage: "insertion.blocked",
                        text: text,
                        details: ["reason": "secure_event_input"])
                    diagnostics.finish(traceID, outcome: "secure_input_blocked", finalText: text)
                    self.onState?(.error(L("err.secure")))
                    return
                }
                let targetApp = NSWorkspace.shared.frontmostApplication
                diagnostics.record(
                    traceID,
                    stage: "insertion.requested",
                    text: text,
                    metrics: [
                        "utf16Units": Double(text.utf16.count),
                        "eventChunks": ceil(Double(text.utf16.count) / 16.0),
                    ],
                    details: [
                        "targetAppName": targetApp?.localizedName ?? "unknown",
                        "targetAppBundleID": targetApp?.bundleIdentifier ?? "unknown",
                    ])
                self.insertText(text)
                diagnostics.record(traceID, stage: "insertion.events_posted", text: text)
                diagnostics.finish(
                    traceID,
                    outcome: "insertion_events_posted",
                    finalText: text)
                self.onState?(.inserted(display))
            }
        }
    }

    /// 闲置一段时间后释放 ASR+标点模型（onnxruntime 常驻内存只增不减，
    /// 卸载是唯一能让内存回落的手段）。下次使用时重新加载，约 1 秒。
    private func scheduleEngineRelease() {
        let w = DispatchWorkItem { [weak self] in
            self?.engine = nil
            self?.engineModelDir = nil
        }
        engineRelease = w
        queue.asyncAfter(deadline: .now() + engineIdleSeconds, execute: w)
    }

    /// 合成键盘 Unicode 事件输入 —— 不经剪贴板
    private func insertText(_ s: String) {
        let src = CGEventSource(stateID: .combinedSessionState)
        let units = Array(s.utf16)
        var i = 0
        while i < units.count {
            let chunk = Array(units[i..<min(i + 16, units.count)])
            for down in [true, false] {
                if let e = CGEvent(keyboardEventSource: src, virtualKey: 0, keyDown: down) {
                    e.keyboardSetUnicodeString(stringLength: chunk.count, unicodeString: chunk)
                    e.post(tap: .cghidEventTap)
                }
            }
            i += 16
        }
    }

    /// 只返回用户当前选择且已完整下载的模型，不做跨模型或开发目录回退。
    static func resolveModelDir(for model: ASRModel) -> String? {
        let item: ModelDownloader.Item
        switch model {
        case .qwen:
            guard ModelDownloader.isReady(ModelDownloader.vad) else { return nil }
            item = ModelDownloader.asrQwen
        case .senseVoice:
            guard ModelDownloader.isReady(ModelDownloader.punct) else { return nil }
            item = ModelDownloader.asrSense
        }
        guard ModelDownloader.isReady(item) else { return nil }
        return ModelDownloader.modelsRoot.appendingPathComponent(item.canonicalDir).path
    }
}
