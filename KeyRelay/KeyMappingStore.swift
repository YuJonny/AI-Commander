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

struct TargetKeyInfo: Equatable {
    var keyCode: UInt16
    var displayName: String
    var isModifier: Bool
    var modifierFlag: CGEventFlags

    static let rightCommand = TargetKeyInfo(
        keyCode: UInt16(kVK_RightCommand),
        displayName: "Right Command ⌘",
        isModifier: true,
        modifierFlag: .maskCommand
    )

    static func from(event: NSEvent) -> TargetKeyInfo? {
        if event.type == .flagsChanged {
            return modifierKeyInfo(keyCode: event.keyCode)
        }
        if event.type == .keyDown {
            let name = readableKeyName(keyCode: event.keyCode, characters: event.charactersIgnoringModifiers)
            return TargetKeyInfo(keyCode: event.keyCode, displayName: name, isModifier: false, modifierFlag: [])
        }
        return nil
    }

    private static func modifierKeyInfo(keyCode: UInt16) -> TargetKeyInfo? {
        switch Int(keyCode) {
        case kVK_RightCommand:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Right Command ⌘", isModifier: true, modifierFlag: .maskCommand)
        case kVK_Command:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Left Command ⌘", isModifier: true, modifierFlag: .maskCommand)
        case kVK_RightOption:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Right Option ⌥", isModifier: true, modifierFlag: .maskAlternate)
        case kVK_Option:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Left Option ⌥", isModifier: true, modifierFlag: .maskAlternate)
        case kVK_RightControl:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Right Control ⌃", isModifier: true, modifierFlag: .maskControl)
        case kVK_Control:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Left Control ⌃", isModifier: true, modifierFlag: .maskControl)
        case kVK_RightShift:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Right Shift ⇧", isModifier: true, modifierFlag: .maskShift)
        case kVK_Shift:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Left Shift ⇧", isModifier: true, modifierFlag: .maskShift)
        case kVK_CapsLock:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Caps Lock ⇪", isModifier: true, modifierFlag: .maskAlphaShift)
        case kVK_Function:
            return TargetKeyInfo(keyCode: keyCode, displayName: "Fn", isModifier: true, modifierFlag: .maskSecondaryFn)
        default:
            return nil
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
        case kVK_F1: return "F1"
        case kVK_F2: return "F2"
        case kVK_F3: return "F3"
        case kVK_F4: return "F4"
        case kVK_F5: return "F5"
        case kVK_F6: return "F6"
        case kVK_F7: return "F7"
        case kVK_F8: return "F8"
        case kVK_F9: return "F9"
        case kVK_F10: return "F10"
        case kVK_F11: return "F11"
        case kVK_F12: return "F12"
        case kVK_Home: return "Home"
        case kVK_End: return "End"
        case kVK_PageUp: return "Page Up"
        case kVK_PageDown: return "Page Down"
        default:
            if let ch = characters?.uppercased(), !ch.isEmpty {
                return ch
            }
            return "Key \(keyCode)"
        }
    }
}

final class KeyMappingStore: ObservableObject {
    static let shared = KeyMappingStore()

    @Published var isEnabled: Bool {
        didSet {
            UserDefaults.standard.set(isEnabled, forKey: "isEnabled")
            if isEnabled {
                interceptor.start()
            } else {
                interceptor.stop()
                statusText = L("status.stopped")
            }
        }
    }

    @Published var targetKey: TargetKeyInfo {
        didSet {
            UserDefaults.standard.set(Int(targetKey.keyCode), forKey: "targetKeyCode")
            UserDefaults.standard.set(targetKey.displayName, forKey: "targetDisplayName")
            UserDefaults.standard.set(targetKey.isModifier, forKey: "targetIsModifier")
            UserDefaults.standard.set(targetKey.modifierFlag.rawValue, forKey: "targetModifierFlag")
            updateInterceptor()
        }
    }

    @Published var isRecording: Bool = false
    @Published var statusText: String = ""

    let interceptor = MediaKeyInterceptor()

    private var localMonitor: Any?
    private var globalMonitor: Any?

    private init() {
        let savedKeyCode = UserDefaults.standard.object(forKey: "targetKeyCode") as? Int
        if let code = savedKeyCode {
            self.targetKey = TargetKeyInfo(
                keyCode: UInt16(code),
                displayName: UserDefaults.standard.string(forKey: "targetDisplayName") ?? "Key \(code)",
                isModifier: UserDefaults.standard.bool(forKey: "targetIsModifier"),
                modifierFlag: CGEventFlags(rawValue: UInt64(UserDefaults.standard.integer(forKey: "targetModifierFlag")))
            )
        } else {
            self.targetKey = .rightCommand
        }

        self.isEnabled = UserDefaults.standard.bool(forKey: "isEnabled")

        interceptor.onStatusChange = { [weak self] text in
            DispatchQueue.main.async {
                self?.statusText = text
            }
        }

        updateInterceptor()
        if isEnabled {
            interceptor.start()
        }
    }

    private func updateInterceptor() {
        interceptor.targetKeyCode = targetKey.keyCode
        interceptor.isModifierKey = targetKey.isModifier
        interceptor.modifierFlag = targetKey.modifierFlag
    }

    func startRecording() {
        isRecording = true

        localMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { [weak self] event in
            self?.captureKey(event: event)
            return nil
        }

        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { [weak self] event in
            self?.captureKey(event: event)
        }
    }

    func stopRecording() {
        isRecording = false
        if let m = localMonitor { NSEvent.removeMonitor(m) }
        if let m = globalMonitor { NSEvent.removeMonitor(m) }
        localMonitor = nil
        globalMonitor = nil
    }

    private func captureKey(event: NSEvent) {
        guard let info = TargetKeyInfo.from(event: event) else { return }
        DispatchQueue.main.async {
            self.targetKey = info
            self.stopRecording()
            if self.isEnabled {
                self.interceptor.start()
            }
        }
    }
}
