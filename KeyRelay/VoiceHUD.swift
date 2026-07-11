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

/// 录音时的「Listening」悬浮提示。
/// - macOS 26+：用真正的 Liquid Glass（NSGlassEffectView）。
/// - 更老系统：回退到 NSVisualEffectView。
/// borderless + nonactivating + 忽略鼠标 → 不抢焦点，合成输入仍落到目标 App。
final class VoiceHUD {
    private var panel: NSPanel?
    private let dot = NSView()
    private let label = NSTextField(labelWithString: "Listening")
    private var hideWork: DispatchWorkItem?

    func render(_ state: VoiceController.State) {
        switch state {
        case .listening:    show(text: "Listening", color: .systemRed)
        case .transcribing: show(text: L("hud.transcribing"), color: .systemOrange)
        case .enhancing:    show(text: L("hud.enhancing"), color: .systemPurple)   // LLM 润色约2s，悬浮窗陪跑到出字
        case .noAudio:      flash(text: L("hud.noaudio"), color: .systemGray)
        case .noTranscript: flash(text: L("hud.notranscript"), color: .systemGray)
        case .error(let e): flash(text: VoiceHUD.shortText(e), color: .systemOrange)
        case .inserted:     hide()
        }
    }

    /// 错误信息浓缩成悬浮窗能放下的短句
    private static func shortText(_ e: String) -> String {
        let s = e.lowercased()
        if e.contains("授权") || e.contains("麦克风") || e.contains("权限")
            || s.contains("mic") || s.contains("author") || s.contains("denied") { return L("hud.grant") }
        if e.contains("隐私") || s.contains("secure") { return L("hud.secure") }
        if e.contains("下载中") || s.contains("downloading") { return L("hud.downloading") }
        if e.contains("下载") || s.contains("download") { return L("hud.needmodel") }
        return e.count > 14 ? String(e.prefix(13)) + "…" : e
    }

    /// 短暂显示后自动隐去（错误/空结果这类一次性提示）
    private func flash(text: String, color: NSColor) {
        show(text: text, color: color)
        let w = DispatchWorkItem { [weak self] in self?.hide() }
        hideWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.5, execute: w)
    }

    private func ensurePanel() {
        guard panel == nil else { return }
        let size = NSSize(width: 172, height: 46)
        let radius = size.height / 2   // 胶囊：圆角=高度一半
        let p = NSPanel(contentRect: NSRect(origin: .zero, size: size),
                        styleMask: [.borderless, .nonactivatingPanel],
                        backing: .buffered, defer: false)
        p.level = .floating
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.ignoresMouseEvents = true
        p.hidesOnDeactivate = false
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]

        // 内容：红色脉冲点 + "Listening"
        dot.wantsLayer = true
        dot.layer?.backgroundColor = NSColor.systemRed.cgColor
        dot.layer?.cornerRadius = 4
        dot.translatesAutoresizingMaskIntoConstraints = false
        label.font = .systemFont(ofSize: 15, weight: .medium)
        label.textColor = .labelColor
        label.translatesAutoresizingMaskIntoConstraints = false

        let stack = NSStackView(views: [dot, label])
        stack.spacing = 9
        stack.alignment = .centerY
        stack.translatesAutoresizingMaskIntoConstraints = false

        let content = NSView(frame: NSRect(origin: .zero, size: size))
        content.autoresizingMask = [.width, .height]
        content.addSubview(stack)
        NSLayoutConstraint.activate([
            dot.widthAnchor.constraint(equalToConstant: 8),
            dot.heightAnchor.constraint(equalToConstant: 8),
            stack.centerXAnchor.constraint(equalTo: content.centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: content.centerYAnchor),
        ])

        if #available(macOS 26.0, *) {
            let glass = NSGlassEffectView(frame: NSRect(origin: .zero, size: size))
            glass.cornerRadius = radius
            glass.contentView = content
            p.contentView = glass
        } else {
            let vev = NSVisualEffectView(frame: NSRect(origin: .zero, size: size))
            vev.material = .hudWindow
            vev.blendingMode = .behindWindow
            vev.state = .active
            vev.wantsLayer = true
            vev.layer?.cornerRadius = radius   // 圆形圆角(默认)=真胶囊
            vev.layer?.masksToBounds = true
            vev.addSubview(content)
            p.contentView = vev
        }
        panel = p
        startPulse()
    }

    private func startPulse() {
        let a = CABasicAnimation(keyPath: "opacity")
        a.fromValue = 1.0; a.toValue = 0.25
        a.duration = 0.6
        a.autoreverses = true
        a.repeatCount = .infinity
        dot.layer?.add(a, forKey: "pulse")
    }

    private func show(text: String, color: NSColor) {
        hideWork?.cancel(); hideWork = nil
        ensurePanel()
        guard let p = panel, let screen = NSScreen.main else { return }
        label.stringValue = text
        dot.layer?.backgroundColor = color.cgColor
        // 宽度随文字自适应（保持胶囊形）
        let tw = (text as NSString).size(withAttributes: [.font: label.font ?? NSFont.systemFont(ofSize: 15)]).width
        let w = max(150, tw + 8 + 9 + 44)
        var f = p.frame
        if p.isVisible {
            f.origin.x += (f.width - w) / 2   // 原地居中变宽，不跳位
            f.size.width = w
        } else {
            let sf = screen.visibleFrame
            f = NSRect(x: sf.midX - w / 2, y: sf.minY + 120, width: w, height: 46)
        }
        p.setFrame(f, display: true)
        p.alphaValue = 1
        p.orderFrontRegardless()     // 显示但不夺焦点
    }

    func hide() {
        hideWork?.cancel(); hideWork = nil
        guard let p = panel, p.isVisible else { return }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.2
            p.animator().alphaValue = 0
        }, completionHandler: { p.orderOut(nil) })
    }
}
