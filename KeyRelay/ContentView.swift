import SwiftUI
import ServiceManagement
import AppKit
import AVFoundation

enum Tab: String, Hashable {
    case function, about, language
}

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

// MARK: - Liquid Glass Card Modifier

extension View {
    /// Liquid glass card: translucent material, subtle border, soft shadow.
    func glassCard(cornerRadius: CGFloat = 16) -> some View {
        self
            .background(
                ZStack {
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(.regularMaterial)
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: [
                                    Color.white.opacity(0.15),
                                    Color.white.opacity(0.02),
                                    Color.clear
                                ],
                                startPoint: .top,
                                endPoint: .bottom
                            )
                        )
                }
            )
            .overlay(
                RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                    .strokeBorder(
                        LinearGradient(
                            colors: [
                                Color.white.opacity(0.4),
                                Color.white.opacity(0.05)
                            ],
                            startPoint: .top,
                            endPoint: .bottom
                        ),
                        lineWidth: 0.5
                    )
            )
            .shadow(color: .black.opacity(0.08), radius: 8, x: 0, y: 2)
    }

    /// Liquid glass tag/pill background.
    func glassTag(tint: Color = .blue) -> some View {
        self
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background(
                Capsule(style: .continuous)
                    .fill(tint.opacity(0.12))
                    .overlay(
                        Capsule(style: .continuous)
                            .strokeBorder(tint.opacity(0.2), lineWidth: 0.5)
                    )
            )
            .foregroundColor(tint)
    }

    /// Inner liquid glass surface for grouped controls.
    func glassPanel(cornerRadius: CGFloat = 10) -> some View {
        self
            .background(
                ZStack {
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(.ultraThinMaterial)
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: [
                                    Color.white.opacity(0.18),
                                    Color.white.opacity(0.04),
                                    Color.clear
                                ],
                                startPoint: .top,
                                endPoint: .bottom
                            )
                        )
                }
            )
            .overlay(
                RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                    .strokeBorder(Color.white.opacity(0.18), lineWidth: 0.5)
            )
    }
}

// MARK: - Main Content View

struct ContentView: View {
    @EnvironmentObject var store: KeyMappingStore
    @State private var selectedTab: Tab = .function
    @State private var refreshID = UUID()

    var body: some View {
        ZStack {
            // Window background — vibrant translucent material
            VisualEffectView(material: .hudWindow, blendingMode: .behindWindow)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                tabBar
                    .padding(.top, 18)
                    .padding(.horizontal, 20)
                    .padding(.bottom, 4)

                ScrollView(showsIndicators: false) {
                    Group {
                        switch selectedTab {
                        case .function:
                            FunctionView(refreshID: $refreshID)
                                .environmentObject(store)
                        case .about:
                            AboutTabView()
                        case .language:
                            LanguageTabView(refreshID: $refreshID)
                        }
                    }
                    .padding(.horizontal, 20)
                    .padding(.top, 12)
                    .padding(.bottom, 20)
                }
            }
        }
        .frame(width: 440, height: 520)
        .id(refreshID)
        .background(
            WindowAccessor { window in
                configureWindow(window)
            }
        )
    }

    private func configureWindow(_ w: NSWindow) {
        w.titlebarAppearsTransparent = true
        w.titleVisibility = .hidden
        w.styleMask.insert(.fullSizeContentView)
        w.isOpaque = false
        w.backgroundColor = .clear
        if let titlebarView = w.standardWindowButton(.closeButton)?.superview?.superview {
            titlebarView.wantsLayer = true
        }
    }

    private var tabBar: some View {
        HStack(spacing: 4) {
            tabPill(title: L("tab.function"), tab: .function)
            tabPill(title: L("tab.about"), tab: .about)
            Spacer()
            tabPill(title: languageTabTitle, tab: .language)
        }
        .padding(4)
        .background(
            Capsule(style: .continuous)
                .fill(.ultraThinMaterial)
                .overlay(
                    Capsule(style: .continuous)
                        .strokeBorder(Color.white.opacity(0.15), lineWidth: 0.5)
                )
        )
    }

    private var languageTabTitle: String {
        currentLanguage == "zh-Hans" ? "Language" : "语言"
    }

    private var currentLanguage: String {
        if let saved = UserDefaults.standard.string(forKey: "appLanguage") { return saved }
        let preferred = Locale.preferredLanguages.first ?? "en"
        return preferred.hasPrefix("zh") ? "zh-Hans" : "en"
    }

    private func tabPill(title: String, tab: Tab) -> some View {
        Button(action: {
            withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) {
                selectedTab = tab
            }
        }) {
            Text(title)
                .font(.system(size: 12, weight: selectedTab == tab ? .semibold : .regular))
                .foregroundColor(selectedTab == tab ? .primary : .secondary)
                .padding(.horizontal, 14)
                .padding(.vertical, 5)
                .background(
                    ZStack {
                        if selectedTab == tab {
                            Capsule(style: .continuous)
                                .fill(.thickMaterial)
                                .overlay(
                                    Capsule(style: .continuous)
                                        .strokeBorder(Color.white.opacity(0.3), lineWidth: 0.5)
                                )
                                .shadow(color: .black.opacity(0.08), radius: 3, y: 1)
                        }
                    }
                )
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Function View

struct FunctionView: View {
    @EnvironmentObject var store: KeyMappingStore
    @State private var hasAccessibility = AccessibilityHelper.isTrusted
    @State private var micStatus = AVCaptureDevice.authorizationStatus(for: .audio)
    @State private var setupGuideDismissed = UserDefaults.standard.bool(forKey: "setupGuideDismissed")
    @State private var checkTimer: Timer?
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var showMethodPicker = false
    @State private var confirmDelete: String? = nil   // "voice"/"llm"：待确认删除的模型
    @Binding var refreshID: UUID
    private let modelActionWidth: CGFloat = 64

    /// 物理内存 <12GB（即 8GB 机型）：润色开关旁提示会变慢，但不禁用
    static let isLowMemoryMachine = ProcessInfo.processInfo.physicalMemory < 12 * 1_073_741_824

    var body: some View {
        VStack(spacing: 12) {
            controlPanel
        }
        .onAppear { startAccessibilityCheck() }
        .onDisappear { checkTimer?.invalidate() }
        .sheet(isPresented: Binding(get: { shouldShowSetupGuide },
                                    set: { _ in })) {
            SetupGuideView(
                hasAccessibility: hasAccessibility,
                micStatus: micStatus,
                headsetDetected: store.headsetKeyDetected,
                isHeadsetTesting: store.isHeadsetKeyTestActive,
                openAccessibility: { AccessibilityHelper.requestPermission() },
                requestMic: { requestMicPermission() },
                testHeadset: { store.startHeadsetKeyTest() },
                dismiss: { dismissSetupGuide() }
            )
            .environmentObject(store)
            .interactiveDismissDisabled(true)
        }
        .alert(String(format: L("model.delete.confirm.title"), confirmDeleteName),
               isPresented: Binding(get: { confirmDelete != nil },
                                    set: { if !$0 { confirmDelete = nil } })) {
            Button(L("model.delete.confirm.ok"), role: .destructive) {
                switch confirmDelete {
                case "qwen":  store.deleteVoiceModels(qwen: true)
                case "sense": store.deleteVoiceModels(qwen: false)
                default:      store.deleteLLMModel()
                }
                confirmDelete = nil
            }
            Button(L("common.cancel"), role: .cancel) { confirmDelete = nil }
        } message: {
            Text(L(confirmDelete == "llm" ? "model.delete.confirm.msg.llm"
                                          : "model.delete.confirm.msg.voice"))
        }
    }

    private var confirmDeleteName: String {
        switch confirmDelete {
        case "qwen":  return L("model.asr.qwen.short")
        case "sense": return L("model.asr.sense.short")
        default:      return L("model.llm.title")
        }
    }

    private var needsRequiredSetup: Bool {
        !hasAccessibility || micStatus != .authorized
    }

    private var shouldShowSetupGuide: Bool {
        needsRequiredSetup || !setupGuideDismissed
    }

    private func dismissSetupGuide() {
        guard !needsRequiredSetup else { return }
        setupGuideDismissed = true
        UserDefaults.standard.set(true, forKey: "setupGuideDismissed")
    }

    private var controlPanel: some View {
        cardContainer {
            VStack(alignment: .leading, spacing: 14) {
                controlHeader

                Divider().opacity(0.35)

                VStack(alignment: .leading, spacing: 8) {
                    sectionLabel(L("input.method"))
                    methodPickerButton
                }

                Divider().opacity(0.35)

                if store.selectedInputMethod == .localLLM {
                    localModelOptions
                } else {
                    mappingDetail
                }

                Divider().opacity(0.35)

                launchAtLoginInline
            }
        }
    }

    private var controlHeader: some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 5) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(L("title.app"))
                        .font(.system(size: 25, weight: .bold, design: .rounded))
                        .foregroundStyle(
                            LinearGradient(
                                colors: [.primary, .primary.opacity(0.75)],
                                startPoint: .top,
                                endPoint: .bottom
                            )
                        )
                    Text(L("title.subtitle"))
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(.secondary)
                }

                HStack(spacing: 6) {
                    Circle()
                        .fill(statusTint)
                        .frame(width: 6, height: 6)
                    Text(statusLabel)
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(.secondary)
                }
            }

            Spacer()

            Toggle("", isOn: $store.isEnabled)
                .toggleStyle(.switch)
                .labelsHidden()
                .controlSize(.regular)
                .disabled(!hasAccessibility)
                .padding(.trailing, 12)   // 与面板内各行的开关右缘对齐（面板 padding=12）
        }
    }

    private var statusLabel: String {
        if needsRequiredSetup { return L("home.status.setup") }
        if store.downloadingVoice || store.downloadingLLM { return L("home.status.downloading") }
        if !store.isEnabled { return L("home.status.off") }
        if store.selectedInputMethod == .localLLM && !currentASRReady {
            return L(store.asrChoiceQwen ? "home.status.needqwen" : "home.status.needsense")
        }
        return L("home.status.ready")
    }

    private var statusTint: Color {
        if needsRequiredSetup { return .orange }
        if store.downloadingVoice || store.downloadingLLM { return .blue }
        if store.selectedInputMethod == .localLLM && store.isEnabled && !currentASRReady { return .orange }
        return store.isEnabled ? .green : .secondary
    }

    private var currentASRReady: Bool {
        store.asrChoiceQwen ? store.qwenReady : (store.senseReady && store.punctReady)
    }

    private var methodPickerButton: some View {
        Button {
            showMethodPicker.toggle()
        } label: {
            HStack(spacing: 10) {
                methodIcon(store.selectedInputMethod, selected: true)
                VStack(alignment: .leading, spacing: 2) {
                    Text(store.selectedInputMethod.displayName)
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundColor(.primary)
                    Text(methodDetailText)
                        .font(.system(size: 10.5))
                        .foregroundColor(.secondary)
                        .lineLimit(1)
                }
                Spacer()
                Image(systemName: "chevron.up.chevron.down")
                    .font(.system(size: 11, weight: .medium))
                    .foregroundColor(.secondary)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(Color.primary.opacity(0.055))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(Color.white.opacity(0.10), lineWidth: 0.5)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!hasAccessibility)
        .popover(isPresented: $showMethodPicker, arrowEdge: .bottom) {
            ScrollView {
                VStack(spacing: 4) {
                    ForEach(InputMethodKind.allCases) { method in
                        methodRow(method)
                    }
                }
                .padding(8)
            }
            .frame(width: 264, height: 384)
        }
    }

    private var methodDetailText: String {
        if store.selectedInputMethod == .localLLM { return L("model.voice.sub") }
        if store.selectedInputMethod == .custom { return L("mapping.custom.hint") }
        return String(format: L("mapping.preset.hint"), store.selectedInputMethod.displayName)
    }

    private var launchAtLoginInline: some View {
        // 复用 rowItem：图标/字号/间距/开关大小与上方各行完全一致
        rowItem(icon: "power",
                title: L("launch.title"), description: L("launch.description")) {
            Toggle("", isOn: $launchAtLogin)
                .toggleStyle(.switch)
                .labelsHidden()
                .onChange(of: launchAtLogin) { newValue in
                    do {
                        if newValue {
                            try SMAppService.mainApp.register()
                        } else {
                            try SMAppService.mainApp.unregister()
                        }
                    } catch {
                        launchAtLogin = !newValue
                    }
                }
        }
        .padding(.horizontal, 12)   // 与面板内各行左右缘对齐（面板 padding=12）
    }

    @ViewBuilder
    private func cardContainer<Content: View>(@ViewBuilder content: () -> Content) -> some View {
        content()
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .glassCard()
    }

    @ViewBuilder
    private func rowItem<Trailing: View>(
        icon: String? = nil,
        title: String,
        description: String,
        @ViewBuilder trailing: () -> Trailing
    ) -> some View {
        HStack(alignment: .center, spacing: 12) {
            if let icon {
                Image(systemName: icon)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(.secondary)
                    .frame(width: 22, height: 22)
                    .background(Circle().fill(Color.primary.opacity(0.06)))
            }
            VStack(alignment: .leading, spacing: 3) {
                Text(title)
                    .font(.system(size: 14, weight: .semibold))
                if !description.isEmpty {
                    Text(description)
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
            }
            Spacer()
            trailing()
        }
    }

    private var micDetail: String {
        switch micStatus {
        case .authorized: return L("setup.mic.ok")
        case .notDetermined: return L("setup.mic.need")
        case .denied, .restricted: return L("setup.mic.denied")
        @unknown default: return L("setup.mic.unknown")
        }
    }

    private func requestMicPermission() {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .notDetermined:
            NSApp.activate(ignoringOtherApps: true)
            AVCaptureDevice.requestAccess(for: .audio) { _ in
                DispatchQueue.main.async {
                    micStatus = AVCaptureDevice.authorizationStatus(for: .audio)
                }
            }
        case .denied, .restricted:
            if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone") {
                NSWorkspace.shared.open(url)
            }
        default:
            micStatus = AVCaptureDevice.authorizationStatus(for: .audio)
        }
    }

    // MARK: 输入方式 子视图

    private func sectionLabel(_ text: String) -> some View {
        HStack {
            Text(text)
                .font(.system(size: 12.5, weight: .semibold))
                .foregroundColor(.secondary)
            Spacer()
        }
    }

    @ViewBuilder
    private func methodRow(_ method: InputMethodKind) -> some View {
        let selected = store.selectedInputMethod == method
        Button {
            withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) {
                store.selectedInputMethod = method
            }
            showMethodPicker = false
        } label: {
            HStack(spacing: 10) {
                methodIcon(method, selected: selected)
                Text(method.displayName)
                    .font(.system(size: 13, weight: selected ? .semibold : .regular))
                    .foregroundColor(.primary)
                Spacer()
                if selected {
                    Image(systemName: "checkmark.circle.fill")
                        .foregroundStyle(Color.accentColor, Color.accentColor.opacity(0.15))
                        .font(.system(size: 15))
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .background(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(selected ? Color.accentColor.opacity(0.1) : Color.clear)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .strokeBorder(selected ? Color.accentColor.opacity(0.3) : Color.white.opacity(0.06), lineWidth: 0.5)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!hasAccessibility)
    }

    @ViewBuilder
    private func compactModelRow(title: String, detail: String, ready: Bool,
                                 active: Bool, fraction: Double,
                                 action: @escaping () -> Void,
                                 cancel: @escaping () -> Void,
                                 onDelete: @escaping () -> Void) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .center, spacing: 8) {
                Image(systemName: "brain.head.profile")
                    .font(.system(size: 9.5, weight: .semibold))
                    .foregroundColor(.secondary)
                    .frame(width: 18, height: 18)
                    .background(Circle().fill(Color.primary.opacity(0.06)))
                HStack(spacing: 4) {
                    Text(title)
                        .font(.system(size: 11.2, weight: .medium))
                        .foregroundColor(.secondary)
                    Text("· \(detail)")
                        .font(.system(size: 10.8))
                        .foregroundColor(.secondary.opacity(0.85))
                }
                .lineLimit(1)
                Spacer()
                modelActionControl(ready: ready, active: active, fraction: fraction,
                                   prominent: false, downloadDisabled: active,
                                   download: action, cancel: cancel, delete: onDelete)
            }
            if active {
                ProgressView(value: fraction)
                    .progressViewStyle(.linear)
                    .controlSize(.small)
                    .tint(.accentColor)
            }
        }
    }

    @ViewBuilder
    private func modelActionControl(
        ready: Bool,
        active: Bool,
        fraction: Double,
        prominent: Bool,
        downloadDisabled: Bool,
        download: @escaping () -> Void,
        cancel: @escaping () -> Void,
        delete: @escaping () -> Void
    ) -> some View {
        Group {
            if ready {
                HStack(spacing: 8) {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.system(size: 12))
                        .foregroundColor(.green)
                        .help(L("model.ready"))
                    Button { delete() } label: {
                        Image(systemName: "trash")
                            .font(.system(size: 10.5, weight: .medium))
                            .frame(width: 22, height: 22)
                    }
                    .buttonStyle(.plain)
                    .foregroundColor(.secondary)
                    .help(L("model.delete.help"))
                }
            } else if active {
                HStack(spacing: 4) {
                    Text("\(Int(fraction * 100))%")
                        .font(.system(size: 12, weight: .semibold).monospacedDigit())
                        .foregroundColor(.accentColor)
                    Button { cancel() } label: {
                        Image(systemName: "xmark.circle.fill")
                            .font(.system(size: 12))
                            .frame(width: 18, height: 22)
                    }
                    .buttonStyle(.plain)
                    .foregroundColor(.secondary)
                    .help(L("dl.cancel"))
                }
            } else {
                modelDownloadButton(prominent: prominent, disabled: downloadDisabled, action: download)
            }
        }
        .frame(width: modelActionWidth, height: 24, alignment: .trailing)
    }

    private func modelDownloadButton(prominent: Bool, disabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 3) {
                Image(systemName: "arrow.down.circle")
                    .font(.system(size: 10.5, weight: .medium))
                Text(L("model.download"))
                    .font(.system(size: 11.5, weight: .semibold))
                    .lineLimit(1)
            }
            .frame(width: 56, height: 22)
            .foregroundColor(prominent ? .accentColor : .secondary)
            .background(
                Capsule(style: .continuous)
                    .fill(prominent ? Color.accentColor.opacity(0.14) : Color.primary.opacity(0.055))
            )
            .overlay(
                Capsule(style: .continuous)
                    .strokeBorder(prominent ? Color.accentColor.opacity(0.28) : Color.white.opacity(0.12), lineWidth: 0.5)
            )
        }
        .buttonStyle(.plain)
        .disabled(disabled)
    }

    // 语音识别模型：两行自述式选项——两档说明常驻可对比（无需先选中才能看介绍）；
    // 选中行 = 运行时实际使用的模型（严格，不回退）；下载/删除只作用于选中行。
    private var asrChoice: some View {
        let active = store.downloadingVoice
        return VStack(alignment: .leading, spacing: 8) {
            sectionLabel(L("model.voice.title"))

            VStack(alignment: .leading, spacing: 0) {
                asrOptionRow(
                    title: L("model.asr.qwen.short"),
                    desc: L("model.asr.qwen.desc"),
                    selected: store.asrChoiceQwen,
                    ready: store.qwenReady,
                    active: active,
                    select: { store.asrChoiceQwen = true },
                    download: { store.downloadQwenASR() },
                    delete: { confirmDelete = "qwen" }
                )

                Divider().opacity(0.3).padding(.vertical, 7)

                asrOptionRow(
                    title: L("model.asr.sense.short"),
                    desc: L("model.asr.sense.desc"),
                    selected: !store.asrChoiceQwen,
                    ready: store.senseReady && store.punctReady,
                    active: active,
                    select: { store.asrChoiceQwen = false },
                    download: { store.downloadSenseASR() },
                    delete: { confirmDelete = "sense" }
                )

                if active {
                    ProgressView(value: store.voiceFraction).progressViewStyle(.linear)
                        .controlSize(.small).tint(.accentColor)
                        .padding(.top, 8)
                }
            }
            .padding(12)
            .glassPanel(cornerRadius: 10)
        }
    }

    // 单行：radio 选中态（就绪=蓝 / 未下载=橙）+ 名称 + 常驻说明 + 行内操作区
    @ViewBuilder
    private func asrOptionRow(
        title: String, desc: String,
        selected: Bool, ready: Bool, active: Bool,
        select: @escaping () -> Void,
        download: @escaping () -> Void,
        delete: @escaping () -> Void
    ) -> some View {
        HStack(alignment: .center, spacing: 10) {
            Image(systemName: selected ? "checkmark.circle.fill" : "circle")
                .font(.system(size: 15))
                .foregroundStyle(selected ? (ready ? Color.accentColor : Color.orange) : Color.secondary.opacity(0.45))

            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.system(size: 12.5, weight: selected ? .semibold : .medium))
                    .foregroundColor(.primary)
                Text(desc)
                    .font(.system(size: 10.5))
                    .foregroundColor(.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }

            Spacer(minLength: 8)

            // 操作区常驻：就绪=✓+删除，未下载=下载按钮（点未选中行的下载会先切到该档），下载中=百分比+取消
            modelActionControl(ready: ready, active: active && selected, fraction: store.voiceFraction,
                               prominent: selected, downloadDisabled: active,
                               download: { if !selected { select() }; download() },
                               cancel: { store.cancelVoiceDownload() },
                               delete: delete)
        }
        .contentShape(Rectangle())
        .onTapGesture {
            guard !active, !selected else { return }   // 下载中锁定切换，进度归属清晰
            withAnimation(.spring(response: 0.3, dampingFraction: 0.85)) { select() }
        }
        .opacity(active && !selected ? 0.55 : 1.0)
    }

    // 前两项用高级渐变徽标；其余用贴合的 SF Symbol
    @ViewBuilder
    private func methodIcon(_ m: InputMethodKind, selected: Bool) -> some View {
        switch m {
        case .localLLM:
            // 接线员透明 logo（assets/logo.png 抠好的版本，无白底）
            if let url = Bundle.main.url(forResource: "localllm", withExtension: "png", subdirectory: "ToolIcons"),
               let img = NSImage(contentsOf: url) {
                Image(nsImage: img)
                    .resizable().interpolation(.high)
                    .scaledToFit()
                    .frame(width: 26, height: 26)
            } else {
                Image(nsImage: NSApplication.shared.applicationIconImage)
                    .resizable().interpolation(.high)
                    .frame(width: 26, height: 26)
            }
        case .custom:
            gradientBadge(colors: [Color(red: 1.0, green: 0.55, blue: 0.3), Color(red: 0.95, green: 0.3, blue: 0.5)],
                          symbol: "slider.horizontal.3")
        default:
            if let icon = m.bundledIcon ?? m.appIcon {
                // 内置官方图标（不装也显示）；兜底本机已安装的图标
                Image(nsImage: icon)
                    .resizable().interpolation(.high)
                    .frame(width: 24, height: 24)
                    .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
                    .frame(width: 26)
            } else {
                Image(systemName: (m == .claudecode || m == .codex) ? "chevron.left.forwardslash.chevron.right" : "mic.fill")
                    .font(.system(size: 12))
                    .foregroundColor(selected ? .accentColor : .secondary)
                    .frame(width: 26, height: 26)
            }
        }
    }

    private func gradientBadge(colors: [Color], symbol: String) -> some View {
        RoundedRectangle(cornerRadius: 7, style: .continuous)
            .fill(LinearGradient(colors: colors, startPoint: .topLeading, endPoint: .bottomTrailing))
            .frame(width: 26, height: 26)
            .overlay(
                Image(systemName: symbol)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(.white)
            )
            .shadow(color: colors[0].opacity(0.4), radius: 3, y: 1)
    }

    // 外部输入法/自定义：展示映射目标，custom 可录制（含组合键）
    private var mappingDetail: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                Text(L("mapping.source"))
                    .font(.system(size: 12, weight: .medium))
                    .glassTag(tint: .blue)
                Image(systemName: "arrow.right")
                    .font(.system(size: 11, weight: .medium))
                    .foregroundColor(.secondary)
                if store.isRecording {
                    Text(L("mapping.recording"))
                        .font(.system(size: 12, weight: .medium))
                        .padding(.horizontal, 10)
                        .padding(.vertical, 4)
                        .background(
                            Capsule(style: .continuous)
                                .strokeBorder(Color.orange, style: StrokeStyle(lineWidth: 1.2, dash: [4]))
                        )
                        .foregroundColor(.orange)
                } else {
                    Text(store.targetKey.displayName)
                        .font(.system(size: 12, weight: .semibold))
                        .glassTag(tint: .green)
                }
                Spacer()
                if store.selectedInputMethod == .custom {
                    if store.isRecording {
                        Button(L("mapping.cancel")) { store.stopRecording() }
                            .buttonStyle(.bordered).controlSize(.small)
                    } else {
                        Button(L("mapping.change")) { store.startRecording() }
                            .buttonStyle(.bordered).controlSize(.small)
                    }
                }
            }
            if store.selectedInputMethod == .custom {
                Text(L("mapping.custom.hint"))
                    .font(.system(size: 11)).foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                Text(String(format: L("mapping.preset.hint"), store.selectedInputMethod.displayName))
                    .font(.system(size: 11)).foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    // 本地大模型：模型下载 + 口语词优化 + 本地 LLM 增强
    // （转写结果不在主窗口展示：文字已键入目标 App，本窗口不留存语音记录）
    private var localModelOptions: some View {
        VStack(alignment: .leading, spacing: 12) {
            asrChoice

            VStack(alignment: .leading, spacing: 10) {
                rowItem(icon: "scissors",
                        title: L("opt.filler.title"), description: L("opt.filler.desc")) {
                    Toggle("", isOn: $store.fillerRules)
                        .toggleStyle(.switch).labelsHidden()
                        .disabled(!hasAccessibility)
                }

                Divider().opacity(0.3)

                rowItem(icon: "sparkles",
                        title: L("opt.polish.title"), description: L("opt.polish.desc")) {
                    Toggle("", isOn: $store.localLLM)
                        .toggleStyle(.switch).labelsHidden()
                        .disabled(!hasAccessibility || !store.llmModelReady)
                }
                // 低内存警告（警告不禁止）：8GB 机型跑 4.7GB 润色模型会明显变慢，提前告知但保留选择权
                if FunctionView.isLowMemoryMachine {
                    HStack(spacing: 5) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .font(.system(size: 9))
                            .foregroundColor(.orange)
                        Text(L("opt.polish.lowmem"))
                            .font(.system(size: 10))
                            .foregroundColor(.orange)
                    }
                    .padding(.leading, 34)   // 与 rowItem 文字列对齐（图标 22 + 间距 12）
                }
                compactModelRow(title: L("model.llm.title"), detail: L("model.llm.sub"),
                                ready: store.llmModelReady,
                                active: store.downloadingLLM, fraction: store.llmFraction,
                                action: { store.downloadLLMModel() },
                                cancel: { store.cancelLLMDownload() },
                                onDelete: { confirmDelete = "llm" })
            }
            .padding(12)
            .glassPanel(cornerRadius: 10)
        }
    }

    private var accessibilityWarning: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundColor(.orange)
                    .font(.system(size: 14))
                Text(L("accessibility.required"))
                    .font(.system(size: 13, weight: .semibold))
                Spacer()
            }
            Text(L("accessibility.description"))
                .font(.system(size: 11))
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Button(L("accessibility.open")) {
                AccessibilityHelper.requestPermission()
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.small)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill(Color.orange.opacity(0.1))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(Color.orange.opacity(0.3), lineWidth: 0.5)
        )
    }

    private func startAccessibilityCheck() {
        hasAccessibility = AccessibilityHelper.isTrusted
        checkTimer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { _ in
            let trusted = AccessibilityHelper.isTrusted
            let currentMic = AVCaptureDevice.authorizationStatus(for: .audio)
            if trusted != hasAccessibility {
                hasAccessibility = trusted
                if trusted && store.isEnabled {
                    store.interceptor.start()
                }
            }
            if currentMic != micStatus {
                micStatus = currentMic
            }
            if !trusted || currentMic != .authorized {
                setupGuideDismissed = false
                UserDefaults.standard.set(false, forKey: "setupGuideDismissed")
            }
        }
    }
}

private struct SetupGuideView: View {
    @EnvironmentObject var store: KeyMappingStore
    let hasAccessibility: Bool
    let micStatus: AVAuthorizationStatus
    let headsetDetected: Bool
    let isHeadsetTesting: Bool
    let openAccessibility: () -> Void
    let requestMic: () -> Void
    let testHeadset: () -> Void
    let dismiss: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            VStack(alignment: .leading, spacing: 6) {
                Text(L("setup.title"))
                    .font(.system(size: 20, weight: .bold, design: .rounded))
                Text(L("setup.description"))
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            VStack(spacing: 10) {
                setupRow(
                    icon: "figure.hand.wave",
                    title: L("setup.accessibility.title"),
                    detail: hasAccessibility ? L("setup.accessibility.ok") : L("setup.accessibility.need"),
                    state: hasAccessibility ? .done : .required,
                    badge: L("setup.required")
                ) {
                    if !hasAccessibility {
                        Button(L("setup.open")) { openAccessibility() }
                            .buttonStyle(.borderedProminent)
                            .controlSize(.small)
                    }
                }

                setupRow(
                    icon: "mic.fill",
                    title: L("setup.mic.title"),
                    detail: micDetail,
                    state: micStatus == .authorized ? .done : .required,
                    badge: L("setup.required")
                ) {
                    if micStatus != .authorized {
                        Button(micStatus == .notDetermined ? L("setup.request") : L("setup.open")) {
                            requestMic()
                        }
                        .buttonStyle(.borderedProminent)
                        .controlSize(.small)
                    }
                }

                setupRow(
                    icon: "headphones",
                    title: L("setup.headset.title"),
                    detail: isHeadsetTesting
                        ? L("setup.headset.testing")
                        : (headsetDetected ? L("setup.headset.ok") : L("setup.headset.need")),
                    state: headsetDetected ? .done : .neutral,
                    badge: L("setup.recommended")
                ) {
                    Button(isHeadsetTesting ? L("setup.testing") : L("setup.test")) {
                        testHeadset()
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.small)
                    .disabled(isHeadsetTesting || !hasAccessibility)
                }
            }

            Text(L("setup.footer"))
                .font(.system(size: 11))
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            HStack {
                Spacer()
                Button(L("setup.done")) { dismiss() }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.regular)
                    .disabled(micStatus != .authorized || !hasAccessibility)
            }
        }
        .padding(22)
        .frame(width: 420)
    }

    private var micDetail: String {
        switch micStatus {
        case .authorized: return L("setup.mic.ok")
        case .notDetermined: return L("setup.mic.need")
        case .denied, .restricted: return L("setup.mic.denied")
        @unknown default: return L("setup.mic.unknown")
        }
    }

    private enum SetupState {
        case done, required, neutral

        var symbol: String {
            switch self {
            case .done: return "checkmark.circle.fill"
            case .required: return "exclamationmark.circle.fill"
            case .neutral: return "circle"
            }
        }

        var color: Color {
            switch self {
            case .done: return .green
            case .required: return .orange
            case .neutral: return .secondary
            }
        }
    }

    @ViewBuilder
    private func setupRow<Trailing: View>(
        icon: String,
        title: String,
        detail: String,
        state: SetupState,
        badge: String,
        @ViewBuilder trailing: () -> Trailing
    ) -> some View {
        HStack(alignment: .center, spacing: 10) {
            Image(systemName: state.symbol)
                .font(.system(size: 15, weight: .semibold))
                .foregroundColor(state.color)
                .frame(width: 18)
            Image(systemName: icon)
                .font(.system(size: 12))
                .foregroundColor(.secondary)
                .frame(width: 18)
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(title)
                        .font(.system(size: 13, weight: .semibold))
                    Text(badge)
                        .font(.system(size: 9.5, weight: .medium))
                        .foregroundColor(state == .required ? .orange : .secondary)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(Capsule().fill(Color.secondary.opacity(0.10)))
                }
                Text(detail)
                    .font(.system(size: 10.8))
                    .foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            trailing()
        }
        .padding(12)
        .background(
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .fill(Color.primary.opacity(0.045))
        )
    }
}

// MARK: - About View

struct AboutTabView: View {
    var body: some View {
        VStack(spacing: 14) {
            // Hero card
            VStack(spacing: 8) {
                if let appIcon = NSApplication.shared.applicationIconImage {
                    Image(nsImage: appIcon)
                        .resizable()
                        .interpolation(.high)
                        .frame(width: 64, height: 64)
                        .shadow(color: .black.opacity(0.15), radius: 8, y: 4)
                }
                Text(L("title.app"))
                    .font(.system(size: 24, weight: .bold, design: .rounded))
                Text(L("title.subtitle"))
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
                Text(L("about.description"))
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                    .multilineTextAlignment(.center)
                Text(L("about.free"))
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                    .padding(.top, 2)
            }
            .padding(.vertical, 20)
            .frame(maxWidth: .infinity)
            .glassCard()

            // Recommended apps card
            VStack(alignment: .leading, spacing: 12) {
                Text(L("about.recommended"))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(.secondary)
                    .textCase(.uppercase)
                    .tracking(0.5)

                VStack(alignment: .leading, spacing: 10) {
                    recommendedRow(
                        icon: "mic.fill",
                        iconColor: .blue,
                        title: L("about.app.typeless"),
                        detail: L("about.app.typeless.desc"),
                        url: "https://www.typeless.com/?via=jonny"
                    )
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .glassCard()

            // Recommended hardware card
            VStack(alignment: .leading, spacing: 12) {
                Text(L("about.recommendedHardware"))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(.secondary)
                    .textCase(.uppercase)
                    .tracking(0.5)

                HStack(alignment: .top, spacing: 10) {
                    Image(systemName: "phone.fill")
                        .foregroundColor(.brown)
                        .font(.system(size: 12))
                        .frame(width: 18)
                    VStack(alignment: .leading, spacing: 3) {
                        Button(L("about.hw.nativeunion")) {
                            if let u = URL(string: "https://s.click.taobao.com/t?e=m%3D2%26s%3DwIrhGUcHebRw4vFB6t2Z2ueEDrYVVa6424t41fwIvtDRnNAdhLhtk430ZFekjizvxL1QHRKL8qiA5rFvxyOF9bwBMngvQdOvBQ7ZDE%2F6%2BIeRXNdY%2BjBkB5eYDQwFj6yC8kftjCRdkjx9Tuff7Tu%2FYJiUD8YEbaigH9ofgWr0NEuwIB%2BqTHya4ovEYPUpsQ%2FF2%2BRbLWXc2uZ5pCmcDdfSii21iRm1aQiYoGwWYjQ2nPkzWlhic%2BUoh0NEJ7GOeqvGxg5p7bh%2BFbQ%3D") {
                                NSWorkspace.shared.open(u)
                            }
                        }
                        .buttonStyle(.link)
                        .font(.system(size: 13))
                        Text(L("about.hw.nativeunion.desc"))
                            .font(.system(size: 11))
                            .foregroundColor(.secondary)
                    }
                    Spacer()
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .glassCard()

            // Developer card
            VStack(alignment: .leading, spacing: 12) {
                Text("Developer")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(.secondary)
                    .textCase(.uppercase)
                    .tracking(0.5)

                VStack(alignment: .leading, spacing: 8) {
                    HStack(spacing: 10) {
                        Image(systemName: "person.crop.circle.fill")
                            .foregroundColor(.secondary)
                            .font(.system(size: 14))
                            .frame(width: 18)
                        Text("Jonny Yu")
                            .font(.system(size: 13))
                    }
                    HStack(spacing: 10) {
                        Image(systemName: "envelope.fill")
                            .foregroundColor(.secondary)
                            .font(.system(size: 12))
                            .frame(width: 18)
                        Button("yujonny@icloud.com") {
                            if let url = URL(string: "mailto:yujonny@icloud.com") {
                                NSWorkspace.shared.open(url)
                            }
                        }
                        .buttonStyle(.link)
                        .font(.system(size: 13))
                    }
                    HStack(spacing: 10) {
                        Image(systemName: "globe")
                            .foregroundColor(.secondary)
                            .font(.system(size: 13))
                            .frame(width: 18)
                        Button("yujonny.com") {
                            if let url = URL(string: "https://yujonny.com") {
                                NSWorkspace.shared.open(url)
                            }
                        }
                        .buttonStyle(.link)
                        .font(.system(size: 13))
                    }
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .glassCard()
        }
    }

    @ViewBuilder
    private func recommendedRow(icon: String, iconColor: Color, title: String, detail: String?, url: String?) -> some View {
        HStack(spacing: 10) {
            Image(systemName: icon)
                .foregroundColor(iconColor)
                .font(.system(size: 12))
                .frame(width: 18)
            if let url = url {
                Button(title) {
                    if let u = URL(string: url) { NSWorkspace.shared.open(u) }
                }
                .buttonStyle(.link)
                .font(.system(size: 13))
            } else {
                Text(title)
                    .font(.system(size: 13))
            }
            if let detail = detail {
                Text(detail)
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
            }
            Spacer()
        }
    }

}

// MARK: - Language View

struct LanguageTabView: View {
    @Binding var refreshID: UUID
    @State private var selectedLang: String

    init(refreshID: Binding<UUID>) {
        _refreshID = refreshID
        let saved = UserDefaults.standard.string(forKey: "appLanguage")
        if let saved = saved {
            _selectedLang = State(initialValue: saved)
        } else {
            let preferred = Locale.preferredLanguages.first ?? "en"
            _selectedLang = State(initialValue: preferred.hasPrefix("zh") ? "zh-Hans" : "en")
        }
    }

    var body: some View {
        VStack(spacing: 16) {
            VStack(spacing: 12) {
                Image(systemName: "globe")
                    .font(.system(size: 38, weight: .light))
                    .foregroundStyle(
                        LinearGradient(
                            colors: [.accentColor, .accentColor.opacity(0.7)],
                            startPoint: .top,
                            endPoint: .bottom
                        )
                    )
                    .padding(.top, 16)

                Text(L("lang.title"))
                    .font(.system(size: 18, weight: .semibold, design: .rounded))

                Text(L("lang.description"))
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
                    .multilineTextAlignment(.center)
                    .padding(.bottom, 4)
            }

            VStack(spacing: 8) {
                langOption(title: "中文", langCode: "zh-Hans")
                langOption(title: "English", langCode: "en")
            }
            .padding(16)
            .frame(maxWidth: .infinity)
            .glassCard()

            Spacer(minLength: 0)
        }
    }

    private func langOption(title: String, langCode: String) -> some View {
        Button(action: {
            withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) {
                selectedLang = langCode
            }
            UserDefaults.standard.set(langCode, forKey: "appLanguage")
            refreshID = UUID()
        }) {
            HStack {
                Text(title)
                    .font(.system(size: 14, weight: selectedLang == langCode ? .semibold : .regular))
                Spacer()
                if selectedLang == langCode {
                    Image(systemName: "checkmark.circle.fill")
                        .foregroundStyle(Color.accentColor, Color.accentColor.opacity(0.15))
                        .font(.system(size: 16))
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(selectedLang == langCode ? Color.accentColor.opacity(0.1) : Color.clear)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(
                        selectedLang == langCode ? Color.accentColor.opacity(0.3) : Color.white.opacity(0.08),
                        lineWidth: 0.5
                    )
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
