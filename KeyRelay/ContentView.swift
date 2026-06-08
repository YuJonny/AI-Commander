import SwiftUI
import ServiceManagement
import AppKit

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
    @State private var checkTimer: Timer?
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @Binding var refreshID: UUID

    var body: some View {
        VStack(spacing: 14) {
            // App title
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(L("title.app"))
                    .font(.system(size: 28, weight: .bold, design: .rounded))
                    .foregroundStyle(
                        LinearGradient(
                            colors: [.primary, .primary.opacity(0.75)],
                            startPoint: .top,
                            endPoint: .bottom
                        )
                    )
                Text(L("title.subtitle"))
                    .font(.system(size: 14, weight: .medium))
                    .foregroundColor(.secondary)
                Spacer()
            }
            .padding(.horizontal, 4)
            .padding(.bottom, 2)

            if !hasAccessibility {
                accessibilityWarning
            }

            // Master switch card
            cardContainer {
                rowItem(
                    title: L("toggle.title"),
                    description: L("toggle.description")
                ) {
                    Toggle("", isOn: $store.isEnabled)
                        .toggleStyle(.switch)
                        .labelsHidden()
                        .controlSize(.regular)
                        .disabled(!hasAccessibility)
                }
            }

            // Mapping card
            cardContainer {
                VStack(alignment: .leading, spacing: 12) {
                    HStack {
                        Text(L("mapping.title"))
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(.secondary)
                            .textCase(.uppercase)
                            .tracking(0.5)
                        Spacer()
                    }

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
                                        .strokeBorder(
                                            Color.orange,
                                            style: StrokeStyle(lineWidth: 1.2, dash: [4])
                                        )
                                )
                                .foregroundColor(.orange)
                        } else {
                            Text(store.targetKey.displayName)
                                .font(.system(size: 12, weight: .semibold))
                                .glassTag(tint: .green)
                        }

                        Spacer()

                        if store.isRecording {
                            Button(L("mapping.cancel")) {
                                store.stopRecording()
                            }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                        } else {
                            Button(L("mapping.change")) {
                                store.startRecording()
                            }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                        }
                    }

                    if !store.isRecording && store.targetKey != .rightCommand {
                        Button {
                            store.targetKey = .rightCommand
                        } label: {
                            HStack(spacing: 4) {
                                Image(systemName: "arrow.counterclockwise")
                                    .font(.system(size: 10))
                                Text(L("mapping.reset"))
                                    .font(.system(size: 11))
                            }
                            .foregroundColor(.secondary)
                        }
                        .buttonStyle(.plain)
                    }
                }
            }

            // Launch at login card
            cardContainer {
                rowItem(
                    title: L("launch.title"),
                    description: L("launch.description")
                ) {
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
            }
        }
        .onAppear { startAccessibilityCheck() }
        .onDisappear { checkTimer?.invalidate() }
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
        title: String,
        description: String,
        @ViewBuilder trailing: () -> Trailing
    ) -> some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text(title)
                    .font(.system(size: 14, weight: .semibold))
                Text(description)
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
            }
            Spacer()
            trailing()
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
            if trusted != hasAccessibility {
                hasAccessibility = trusted
                if trusted && store.isEnabled {
                    store.interceptor.start()
                }
            }
        }
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
                Text("Commander")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
                Text(L("about.description"))
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                    .multilineTextAlignment(.center)
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
