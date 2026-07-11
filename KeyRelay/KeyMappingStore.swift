import Foundation
import Combine
import Carbon.HIToolbox
import AppKit

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

// MARK: - 映射目标（支持单键 / 单修饰键 / 多键组合）

struct TargetKeyInfo: Equatable {
    var keyCode: UInt16
    var displayName: String
    var isModifier: Bool            // true = 纯修饰键（如 ⌘）；false = 普通键，可带 modifierFlag 组合
    var modifierFlag: CGEventFlags  // isModifier 时=该修饰键；普通键时=要一起按住的修饰键组合（可为空）
    var isDoubleTap: Bool = false   // 双击该键触发（保留给历史配置和自定义目标）

    static let rightCommand = TargetKeyInfo(
        keyCode: UInt16(kVK_RightCommand),
        displayName: "Right Command ⌘",
        isModifier: true,
        modifierFlag: .maskCommand
    )

    /// 从一次 keyDown 构造目标：无修饰=普通键；有修饰=组合键（⌘⇧Space 等）。
    static func fromKeyDown(_ e: NSEvent) -> TargetKeyInfo {
        let mods = cgFlags(from: e.modifierFlags)
        let base = readableKeyName(keyCode: e.keyCode, characters: e.charactersIgnoringModifiers)
        if mods.isEmpty {
            return TargetKeyInfo(keyCode: e.keyCode, displayName: base, isModifier: false, modifierFlag: [])
        }
        return TargetKeyInfo(keyCode: e.keyCode,
                             displayName: modifierSymbols(e.modifierFlags) + base,
                             isModifier: false, modifierFlag: mods)
    }

    static func cgFlags(from m: NSEvent.ModifierFlags) -> CGEventFlags {
        var f: CGEventFlags = []
        if m.contains(.command)  { f.insert(.maskCommand) }
        if m.contains(.option)   { f.insert(.maskAlternate) }
        if m.contains(.control)  { f.insert(.maskControl) }
        if m.contains(.shift)    { f.insert(.maskShift) }
        if m.contains(.function) { f.insert(.maskSecondaryFn) }
        if m.contains(.capsLock) { f.insert(.maskAlphaShift) }
        return f
    }

    static func modifierSymbols(_ m: NSEvent.ModifierFlags) -> String {
        var s = ""
        if m.contains(.control)  { s += "⌃" }
        if m.contains(.option)   { s += "⌥" }
        if m.contains(.shift)    { s += "⇧" }
        if m.contains(.command)  { s += "⌘" }
        if m.contains(.function) { s += "fn " }
        return s
    }

    static func modifierKeyInfo(keyCode: UInt16) -> TargetKeyInfo? {
        switch Int(keyCode) {
        case kVK_RightCommand: return TargetKeyInfo(keyCode: keyCode, displayName: "Right Command ⌘", isModifier: true, modifierFlag: .maskCommand)
        case kVK_Command:      return TargetKeyInfo(keyCode: keyCode, displayName: "Left Command ⌘", isModifier: true, modifierFlag: .maskCommand)
        case kVK_RightOption:  return TargetKeyInfo(keyCode: keyCode, displayName: "Right Option ⌥", isModifier: true, modifierFlag: .maskAlternate)
        case kVK_Option:       return TargetKeyInfo(keyCode: keyCode, displayName: "Left Option ⌥", isModifier: true, modifierFlag: .maskAlternate)
        case kVK_RightControl: return TargetKeyInfo(keyCode: keyCode, displayName: "Right Control ⌃", isModifier: true, modifierFlag: .maskControl)
        case kVK_Control:      return TargetKeyInfo(keyCode: keyCode, displayName: "Left Control ⌃", isModifier: true, modifierFlag: .maskControl)
        case kVK_RightShift:   return TargetKeyInfo(keyCode: keyCode, displayName: "Right Shift ⇧", isModifier: true, modifierFlag: .maskShift)
        case kVK_Shift:        return TargetKeyInfo(keyCode: keyCode, displayName: "Left Shift ⇧", isModifier: true, modifierFlag: .maskShift)
        case kVK_CapsLock:     return TargetKeyInfo(keyCode: keyCode, displayName: "Caps Lock ⇪", isModifier: true, modifierFlag: .maskAlphaShift)
        case kVK_Function:     return TargetKeyInfo(keyCode: keyCode, displayName: "fn", isModifier: true, modifierFlag: .maskSecondaryFn)
        default: return nil
        }
    }

    private static func readableKeyName(keyCode: UInt16, characters: String?) -> String {
        switch Int(keyCode) {
        case kVK_Space: return "Space"
        case kVK_Return: return "Return ↩"
        case kVK_Escape: return "Escape"
        case kVK_Tab: return "Tab ⇥"
        case kVK_Delete: return "Delete ⌫"
        case kVK_ForwardDelete: return "Forward Delete ⌦"
        case kVK_UpArrow: return "↑"
        case kVK_DownArrow: return "↓"
        case kVK_LeftArrow: return "←"
        case kVK_RightArrow: return "→"
        case kVK_F1: return "F1"; case kVK_F2: return "F2"; case kVK_F3: return "F3"
        case kVK_F4: return "F4"; case kVK_F5: return "F5"; case kVK_F6: return "F6"
        case kVK_F7: return "F7"; case kVK_F8: return "F8"; case kVK_F9: return "F9"
        case kVK_F10: return "F10"; case kVK_F11: return "F11"; case kVK_F12: return "F12"
        case kVK_Home: return "Home"; case kVK_End: return "End"
        case kVK_PageUp: return "Page Up"; case kVK_PageDown: return "Page Down"
        default:
            if let ch = characters?.uppercased(), !ch.isEmpty { return ch }
            return "Key \(keyCode)"
        }
    }
}

// MARK: - 输入方式（预设输入法 / 本地大模型 / 自定义）

enum InputMethodKind: String, CaseIterable, Identifiable {
    // 顺序即展示顺序：本地大模型 → 自定义 → 各输入法/工具
    case localLLM, custom, doubao, wechat, typeless, flashsay, wisprflow, superwhisper, autoglm, claudecode, codex
    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .localLLM:     return L("method.localLLM")
        case .custom:       return L("method.custom")
        case .doubao:       return "豆包输入法"
        case .wechat:       return "微信输入法"
        case .typeless:     return "Typeless"
        case .flashsay:     return "闪电说"
        case .wisprflow:    return "Wispr Flow"
        case .superwhisper: return "Superwhisper"
        case .autoglm:      return "AutoGLM"
        case .claudecode:   return "Claude Code"
        case .codex:        return "Codex"
        }
    }

    var isLocalVoice: Bool { self == .localLLM }

    /// 选中该输入法时把耳机键映射到的默认快捷键（据各家官方默认，2026-07 查证）。
    /// nil = localLLM（走本地录音）或 custom（用户自定义）。
    var presetTarget: TargetKeyInfo? {
        func fnKey() -> TargetKeyInfo {
            TargetKeyInfo(keyCode: UInt16(kVK_Function), displayName: "fn",
                          isModifier: true, modifierFlag: .maskSecondaryFn)
        }
        switch self {
        case .localLLM, .custom:
            return nil
        case .doubao, .wechat, .typeless:
            return fnKey()
        case .wisprflow:                                      // Hands-free：fn+Space 按一下开始/再按结束
            return TargetKeyInfo(keyCode: UInt16(kVK_Space), displayName: "fn Space",
                                 isModifier: false, modifierFlag: .maskSecondaryFn)
        case .flashsay:                                       // "自由说"：右 ⌘ 按一下开始/结束
            return TargetKeyInfo(keyCode: UInt16(kVK_RightCommand), displayName: "Right Command ⌘",
                                 isModifier: true, modifierFlag: .maskCommand)
        case .superwhisper, .autoglm:                         // 均默认 ⌥Space（AutoGLM：按一下开始/再按结束，2026-07 官方默认）
            return TargetKeyInfo(keyCode: UInt16(kVK_Space), displayName: "⌥ Space",
                                 isModifier: false, modifierFlag: .maskAlternate)
        case .claudecode:                                     // Claude Code 语音输入：Caps Lock，按一下开始/再按结束
            return TargetKeyInfo(keyCode: UInt16(kVK_CapsLock), displayName: "Caps Lock ⇪",
                                 isModifier: true, modifierFlag: .maskAlphaShift)
        case .codex:                                          // Codex 当前 composer：直接启动听写
            return TargetKeyInfo(keyCode: UInt16(kVK_ANSI_D), displayName: "⌃⇧D",
                                 isModifier: false, modifierFlag: [.maskControl, .maskShift])
        }
    }

    /// 加载本机已安装应用的官方图标（bundle id 优先，其次按名字找）。
    var appHints: (ids: [String], names: [String]) {
        switch self {
        case .doubao:       return (["com.bytedance.doubao.inputmethod"], ["豆包输入法"])
        case .wechat:       return (["com.tencent.inputmethod.wetype"], ["微信输入法", "WeType"])
        case .typeless:     return (["now.typeless.desktop"], ["Typeless"])
        case .flashsay:     return (["cn.shandianshuo.desktop"], ["闪电说"])
        case .wisprflow:    return (["com.electron.wisprflow", "ai.wisprflow.desktop"], ["Wispr Flow", "Flow"])
        case .superwhisper: return (["com.superduper.superwhisper"], ["superwhisper"])
        case .autoglm:      return (["cn.zhipuai.autoglm", "com.zhipuai.autoglm"], ["AutoGLM", "智谱AI输入法"])
        case .claudecode:   return (["com.anthropic.claudefordesktop"], ["Claude"])  // CLI 壳无图标，用桌面版 Claude logo
        case .codex:        return (["com.openai.codex"], ["Codex"])
        case .localLLM, .custom: return ([], [])
        }
    }

    /// 内置官方图标（Resources/ToolIcons/<rawValue>.png），不依赖本机是否安装。
    var bundledIcon: NSImage? {
        guard self != .localLLM, self != .custom else { return nil }
        if let url = Bundle.main.url(forResource: rawValue, withExtension: "png", subdirectory: "ToolIcons") {
            return NSImage(contentsOf: url)
        }
        return nil
    }

    /// 本机已安装应用的图标（作为内置图标的兜底）。
    var appIcon: NSImage? {
        let ws = NSWorkspace.shared
        let (ids, names) = appHints
        for id in ids {
            if let u = ws.urlForApplication(withBundleIdentifier: id) { return ws.icon(forFile: u.path) }
        }
        for n in names {
            if let u = Self.installedAppURL(named: n) { return ws.icon(forFile: u.path) }
        }
        return nil
    }

    private static func installedAppURL(named name: String) -> URL? {
        let fm = FileManager.default
        let appName = name.hasSuffix(".app") ? name : "\(name).app"
        let roots = [
            URL(fileURLWithPath: "/Applications"),
            URL(fileURLWithPath: "/System/Applications"),
            fm.homeDirectoryForCurrentUser.appendingPathComponent("Applications")
        ]
        for root in roots {
            let url = root.appendingPathComponent(appName)
            if fm.fileExists(atPath: url.path) { return url }
        }
        return nil
    }
}

final class KeyMappingStore: ObservableObject {
    static let shared = KeyMappingStore()

    // 模块①：总开关
    @Published var isEnabled: Bool {
        didSet {
            UserDefaults.standard.set(isEnabled, forKey: "isEnabled")
            if isEnabled {
                applyInputMethod()
            } else {
                interceptor.stop()
                statusText = L("status.stopped")
            }
        }
    }

    // 模块②：输入方式
    @Published var selectedInputMethod: InputMethodKind {
        didSet {
            UserDefaults.standard.set(selectedInputMethod.rawValue, forKey: "inputMethod")
            applyInputMethod()
        }
    }
    /// 自定义模式下用户录制的目标（可为组合键）。
    @Published var customTarget: TargetKeyInfo {
        didSet {
            Self.saveTarget(customTarget, "custom")
            if selectedInputMethod == .custom { applyInputMethod() }
        }
    }
    /// 当前生效的映射目标（展示 + 喂给 interceptor）。
    @Published private(set) var targetKey: TargetKeyInfo = .rightCommand

    // 本地大模型子选项
    @Published var fillerRules: Bool {            // 口语词优化
        didSet {
            UserDefaults.standard.set(fillerRules, forKey: "fillerRules")
            voiceController.cleanFillers = fillerRules
        }
    }
    @Published var localLLM: Bool {               // 本地 LLM 增强（内嵌 llama.cpp + Qwen3-8B）
        didSet {
            UserDefaults.standard.set(localLLM, forKey: "localLLM")
            voiceController.useLLM = localLLM
            if !localLLM { LlamaRunner.shared.stop() }   // 关掉立即退出 server、释放内存
        }
    }

    // 模型下载状态（驱动下载按钮 + 进度条；下载都由用户点按钮显式触发）
    @Published var voiceModelReady: Bool = false
    @Published var qwenReady: Bool = false
    @Published var senseReady: Bool = false
    @Published var punctReady: Bool = false
    @Published var llmModelReady: Bool = false
    @Published var asrChoiceQwen: Bool = true { // 同时是 UI 选择和运行时唯一选择
        didSet {
            UserDefaults.standard.set(asrChoiceQwen, forKey: "asrChoiceQwen")
            voiceController.selectModel(asrChoiceQwen ? .qwen : .senseVoice)
        }
    }
    @Published var downloadingKind: String? = nil {    // "voice" / "llm"
        didSet { voiceController.isVoiceModelDownloading = downloadingKind == "voice" }
    }
    @Published var downloadFraction: Double = 0
    @Published var headsetKeyDetected: Bool {
        didSet { UserDefaults.standard.set(headsetKeyDetected, forKey: "headsetKeyDetected") }
    }
    @Published var isHeadsetKeyTestActive: Bool = false

    func refreshModelStates() {
        qwenReady = ModelDownloader.isReady(ModelDownloader.asrQwen)
            && ModelDownloader.isReady(ModelDownloader.vad)
        senseReady = ModelDownloader.isReady(ModelDownloader.asrSense)
        punctReady = ModelDownloader.isReady(ModelDownloader.punct)
        voiceModelReady = ModelDownloader.anyASRReady
        llmModelReady = LlamaRunner.modelPath != nil
    }

    // 下载前磁盘预检查：需求 = 下载体积 + 解压/校验余量。不够就明确告知，别下到 99% 才失败。
    func downloadQwenASR()  { guard ensureDiskSpace(neededGB: 2.5) else { return }
                              startDownload(kind: "voice") { ModelDownloader.shared.startQwen() } }
    func downloadSenseASR() { guard ensureDiskSpace(neededGB: 0.8) else { return }
                              startDownload(kind: "voice") { ModelDownloader.shared.startSense() } }
    func downloadLLMModel()    { guard ensureDiskSpace(neededGB: 5.5) else { return }
                                 startDownload(kind: "llm")   { ModelDownloader.shared.startLLM() } }

    private func ensureDiskSpace(neededGB: Double) -> Bool {
        let root = ModelDownloader.modelsRoot
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        guard let free = (try? root.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey]))?
            .volumeAvailableCapacityForImportantUsage else { return true }   // 查不到就不拦
        let freeGB = Double(free) / 1_073_741_824
        guard freeGB < neededGB else { return true }
        DispatchQueue.main.async {
            let alert = NSAlert()
            alert.messageText = L("dl.space.title")
            alert.informativeText = String(
                format: L("dl.space.msg"),
                String(format: "%.1f GB", neededGB),
                String(format: "%.1f GB", freeGB))
            alert.alertStyle = .warning
            alert.runModal()
        }
        return false
    }

    /// 删除指定档的语音模型（按钮常驻两行，可删任意一档，不限于当前选中档）
    func deleteVoiceModels(qwen: Bool) {
        let fm = FileManager.default
        let dirs = qwen
            ? [ModelDownloader.asrQwen.canonicalDir, ModelDownloader.vad.canonicalDir]
            : [ModelDownloader.asrSense.canonicalDir, ModelDownloader.punct.canonicalDir]
        for d in dirs {
            try? fm.removeItem(at: ModelDownloader.modelsRoot.appendingPathComponent(d))
        }
        if qwen == asrChoiceQwen { voiceController.invalidateEngine() }
        refreshModelStates()
        // 删的是当前选中档且另一档完整可用 → 自动切换过去
        if qwen == asrChoiceQwen {
            if asrChoiceQwen, senseReady && punctReady {
                asrChoiceQwen = false
            } else if !asrChoiceQwen, qwenReady {
                asrChoiceQwen = true
            }
        }
        statusText = L("del.voice.done")
    }

    func deleteLLMModel() {
        LlamaRunner.shared.stop()
        if localLLM { localLLM = false }   // 模型没了，润色自动关
        try? FileManager.default.removeItem(at: ModelDownloader.modelsRoot.appendingPathComponent(ModelDownloader.llm.canonicalDir))
        refreshModelStates()
        statusText = L("del.llm.done")
    }

    private func startDownload(kind: String, _ begin: @escaping () -> Void) {
        guard !ModelDownloader.shared.isRunning else { return }
        downloadingKind = kind
        downloadFraction = 0
        let dl = ModelDownloader.shared
        dl.onProgress = { [weak self] text in self?.statusText = text }
        dl.onFraction = { [weak self] f in self?.downloadFraction = f }
        dl.onFinished = { [weak self] _ in
            self?.downloadingKind = nil
            self?.refreshModelStates()
        }
        begin()
    }

    @Published var isRecording: Bool = false      // 正在录制"自定义"按键
    @Published var statusText: String = ""
    @Published var lastTranscript: String = ""

    let interceptor = MediaKeyInterceptor()
    let voiceController = VoiceController()
    let voiceHUD = VoiceHUD()

    private var localMonitor: Any?
    private var globalMonitor: Any?
    private var pendingModifier: TargetKeyInfo?
    private var headsetTestWorkItem: DispatchWorkItem?

    private init() {
        let d = UserDefaults.standard
        self.isEnabled = d.bool(forKey: "isEnabled")
        self.selectedInputMethod = InputMethodKind(rawValue: d.string(forKey: "inputMethod") ?? "") ?? .localLLM
        self.customTarget = Self.loadTarget("custom", default: .rightCommand)
        self.fillerRules = d.object(forKey: "fillerRules") == nil ? true : d.bool(forKey: "fillerRules")
        self.localLLM = d.bool(forKey: "localLLM")
        self.headsetKeyDetected = d.bool(forKey: "headsetKeyDetected")
        if d.object(forKey: "asrChoiceQwen") != nil {
            self.asrChoiceQwen = d.bool(forKey: "asrChoiceQwen")
        } else {
            // 老用户没有保存过选择时：仅安装 SenseVoice 就沿用它，否则默认 Qwen3。
            let qwenReady = ModelDownloader.isReady(ModelDownloader.asrQwen)
                && ModelDownloader.isReady(ModelDownloader.vad)
            let senseOnly = !qwenReady
                && ModelDownloader.isReady(ModelDownloader.asrSense)
                && ModelDownloader.isReady(ModelDownloader.punct)
            self.asrChoiceQwen = !senseOnly
        }

        interceptor.onStatusChange = { [weak self] text in
            DispatchQueue.main.async { self?.statusText = text }
        }
        interceptor.onMediaKeyDown = { [weak self] in
            self?.markHeadsetKeyDetected()
        }
        interceptor.onVoiceTrigger = { [weak self] in
            self?.voiceController.toggle()
        }
        voiceController.onState = { [weak self] state in
            DispatchQueue.main.async {
                guard let self else { return }
                self.statusText = state.icon + " " + state.text
                if case .inserted(let t) = state { self.lastTranscript = t }
                self.voiceHUD.render(state)
            }
        }
        voiceController.cleanFillers = fillerRules
        voiceController.useLLM = localLLM
        voiceController.selectModel(asrChoiceQwen ? .qwen : .senseVoice)

        refreshModelStates()
        applyInputMethod()
    }

    func startHeadsetKeyTest() {
        headsetTestWorkItem?.cancel()
        isHeadsetKeyTestActive = true
        headsetKeyDetected = false
        interceptor.detectionOnly = true
        interceptor.start()

        let work = DispatchWorkItem { [weak self] in
            self?.finishHeadsetKeyTest(detected: false)
        }
        headsetTestWorkItem = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 8, execute: work)
    }

    private func markHeadsetKeyDetected() {
        headsetKeyDetected = true
        if isHeadsetKeyTestActive {
            finishHeadsetKeyTest(detected: true)
        }
    }

    private func finishHeadsetKeyTest(detected: Bool) {
        headsetTestWorkItem?.cancel()
        headsetTestWorkItem = nil
        isHeadsetKeyTestActive = false
        if detected { headsetKeyDetected = true }
        interceptor.detectionOnly = false
        if isEnabled {
            applyInputMethod()
        } else {
            interceptor.stop()
        }
    }

    /// 根据当前输入方式：设定 voiceMode / 映射目标，并按总开关启停拦截器。
    private func applyInputMethod() {
        interceptor.voiceMode = selectedInputMethod.isLocalVoice
        if let preset = selectedInputMethod.presetTarget {
            targetKey = preset
        } else if selectedInputMethod == .custom {
            targetKey = customTarget
        }
        updateInterceptor()
        if isEnabled {
            interceptor.start()
            if interceptor.voiceMode {
                voiceController.ensureMicPermission()
                // 模型不再静默自动下——由界面上的下载按钮显式触发
            }
        } else {
            interceptor.stop()
        }
    }

    private func updateInterceptor() {
        interceptor.targetKeyCode = targetKey.keyCode
        interceptor.isModifierKey = targetKey.isModifier
        interceptor.modifierFlag = targetKey.modifierFlag
        interceptor.isDoubleTap = targetKey.isDoubleTap
    }

    // MARK: 录制"自定义"按键（支持组合键）

    func startRecording() {
        isRecording = true
        pendingModifier = nil
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { [weak self] e in
            self?.captureKey(event: e); return nil
        }
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { [weak self] e in
            self?.captureKey(event: e)
        }
    }

    func stopRecording() {
        isRecording = false
        pendingModifier = nil
        if let m = localMonitor { NSEvent.removeMonitor(m) }
        if let m = globalMonitor { NSEvent.removeMonitor(m) }
        localMonitor = nil; globalMonitor = nil
    }

    private func captureKey(event: NSEvent) {
        if event.type == .keyDown {
            // keyDown：普通键或组合键，立即确定
            commitCapture(TargetKeyInfo.fromKeyDown(event))
        } else if event.type == .flagsChanged {
            let active = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            if active.isEmpty {
                // 修饰键全部松开：若之前按过单修饰键且没接普通键 → 记为纯修饰键
                if let p = pendingModifier { commitCapture(p) }
            } else if let info = TargetKeyInfo.modifierKeyInfo(keyCode: event.keyCode) {
                pendingModifier = info   // 先记住，等看是否接普通键组成组合
            }
        }
    }

    private func commitCapture(_ info: TargetKeyInfo) {
        DispatchQueue.main.async {
            if self.selectedInputMethod != .custom { self.selectedInputMethod = .custom }
            self.customTarget = info
            self.stopRecording()
        }
    }

    // MARK: 持久化 TargetKeyInfo

    private static func loadTarget(_ p: String, default def: TargetKeyInfo) -> TargetKeyInfo {
        let d = UserDefaults.standard
        guard d.object(forKey: p + "KeyCode") != nil else { return def }
        return TargetKeyInfo(
            keyCode: UInt16(d.integer(forKey: p + "KeyCode")),
            displayName: d.string(forKey: p + "Name") ?? def.displayName,
            isModifier: d.bool(forKey: p + "IsMod"),
            modifierFlag: CGEventFlags(rawValue: UInt64(d.integer(forKey: p + "Flag"))),
            isDoubleTap: d.bool(forKey: p + "Dbl"))
    }

    private static func saveTarget(_ t: TargetKeyInfo, _ p: String) {
        let d = UserDefaults.standard
        d.set(Int(t.keyCode), forKey: p + "KeyCode")
        d.set(t.displayName, forKey: p + "Name")
        d.set(t.isModifier, forKey: p + "IsMod")
        d.set(Int(t.modifierFlag.rawValue), forKey: p + "Flag")
        d.set(t.isDoubleTap, forKey: p + "Dbl")
    }
}
