import SwiftUI
import ServiceManagement

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

@main
struct CommanderApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(KeyMappingStore.shared)
        }
        .windowStyle(.hiddenTitleBar)
        .windowResizability(.contentSize)
    }
}

class AppDelegate: NSObject, NSApplicationDelegate, ObservableObject {
    private var statusItem: NSStatusItem?
    private var window: NSWindow?
    private var windowObserver: Any?

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupStatusBar()

        // Start with Dock icon visible (window will be shown)
        NSApp.setActivationPolicy(.regular)

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
            if let w = NSApp.windows.first {
                self.window = w
                w.isReleasedWhenClosed = false
                w.styleMask.remove(.resizable)
                w.setContentSize(NSSize(width: 440, height: 520))
                // Liquid Glass window setup: transparent titlebar, translucent background
                w.titlebarAppearsTransparent = true
                w.titleVisibility = .hidden
                w.styleMask.insert(.fullSizeContentView)
                w.isOpaque = false
                w.backgroundColor = .clear
                w.center()
                self.observeWindow(w)
            }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ application: NSApplication) -> Bool {
        false
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag {
            showWindow()
        }
        return true
    }

    /// Watch for window close → hide Dock icon; window open → show Dock icon
    private func observeWindow(_ w: NSWindow) {
        // 先摘旧观察者再挂新：block 型观察者不显式 remove 会泄漏，且同一窗口会重复触发
        if let old = windowObserver {
            NotificationCenter.default.removeObserver(old)
        }
        // When window becomes visible, show Dock icon
        windowObserver = NotificationCenter.default.addObserver(
            forName: NSWindow.willCloseNotification,
            object: w,
            queue: .main
        ) { [weak self] _ in
            // Window closed → hide from Dock (no flash since window is already gone)
            NSApp.setActivationPolicy(.accessory)
            _ = self // prevent unused warning
        }
    }

    private func setupStatusBar() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)

        if let button = statusItem?.button {
            button.image = makeStatusBarIcon()
        }

        let menu = NSMenu()
        let store = KeyMappingStore.shared

        let titleItem = NSMenuItem(title: L("menu.title"), action: nil, keyEquivalent: "")
        titleItem.attributedTitle = NSAttributedString(
            string: L("menu.title"),
            attributes: [.font: NSFont.boldSystemFont(ofSize: 13)]
        )
        menu.addItem(titleItem)

        menu.addItem(NSMenuItem.separator())

        // 开关（勾选样式）：总开关 + 本地大模型润色
        let masterItem = NSMenuItem(title: L("menu.master"), action: #selector(toggleMapping), keyEquivalent: "")
        masterItem.target = self
        masterItem.tag = 101
        masterItem.state = store.isEnabled ? .on : .off
        menu.addItem(masterItem)

        let polishItem = NSMenuItem(title: L("menu.polish"), action: #selector(togglePolish), keyEquivalent: "")
        polishItem.target = self
        polishItem.tag = 102
        polishItem.state = store.localLLM ? .on : .off
        polishItem.isEnabled = store.llmModelReady
        menu.addItem(polishItem)

        let showItem = NSMenuItem(title: L("menu.open"), action: #selector(showWindow), keyEquivalent: "")
        showItem.target = self
        menu.addItem(showItem)

        menu.addItem(NSMenuItem.separator())

        let quitItem = NSMenuItem(title: L("menu.quit"), action: #selector(quitApp), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)

        self.statusItem?.menu = menu

        NotificationCenter.default.addObserver(forName: UserDefaults.didChangeNotification, object: nil, queue: .main) { [weak self] _ in
            self?.updateMenu()
        }
    }

    private func makeStatusBarIcon() -> NSImage {
        let bundle = Bundle.main
        let image: NSImage
        if let url2x = bundle.url(forResource: "statusbar_icon@2x", withExtension: "png"),
           let img = NSImage(contentsOf: url2x) {
            img.size = NSSize(width: 18, height: 18)
            image = img
        } else if let url1x = bundle.url(forResource: "statusbar_icon", withExtension: "png"),
                  let img = NSImage(contentsOf: url1x) {
            img.size = NSSize(width: 18, height: 18)
            image = img
        } else {
            image = NSImage(size: NSSize(width: 18, height: 18))
        }
        image.isTemplate = true
        return image
    }

    private func updateMenu() {
        guard let menu = statusItem?.menu else { return }
        let store = KeyMappingStore.shared
        if let item = menu.item(withTag: 101) { item.state = store.isEnabled ? .on : .off }
        if let item = menu.item(withTag: 102) {
            item.state = store.localLLM ? .on : .off
            item.isEnabled = store.llmModelReady
        }
    }

    @objc private func toggleMapping() {
        KeyMappingStore.shared.isEnabled.toggle()
    }

    @objc private func togglePolish() {
        KeyMappingStore.shared.localLLM.toggle()
    }

    @objc private func showWindow() {
        // Show Dock icon first, then show window — no flash because window appears after policy change
        NSApp.setActivationPolicy(.regular)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
            if let w = self.window {
                w.isReleasedWhenClosed = false
                w.center()
                w.makeKeyAndOrderFront(nil)
                self.observeWindow(w)
            } else if let w = NSApp.windows.first {
                self.window = w
                w.isReleasedWhenClosed = false
                w.center()
                w.makeKeyAndOrderFront(nil)
                self.observeWindow(w)
            }
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    @objc private func quitApp() {
        NSApp.terminate(nil)
    }
}
