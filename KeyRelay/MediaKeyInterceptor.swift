import Foundation
import CoreGraphics
import AppKit
import MediaPlayer

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

final class MediaKeyInterceptor {
    var targetKeyCode: UInt16 = 54
    var isModifierKey: Bool = true
    var modifierFlag: CGEventFlags = .maskCommand

    var onStatusChange: ((String) -> Void)?

    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var commandTargets: [Any] = []

    func start() {
        stop()
        startEventTap()
        startRemoteCommandBlock()
    }

    func stop() {
        if let tap = eventTap {
            CGEvent.tapEnable(tap: tap, enable: false)
        }
        if let source = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes)
        }
        eventTap = nil
        runLoopSource = nil

        let center = MPRemoteCommandCenter.shared()
        for target in commandTargets {
            center.togglePlayPauseCommand.removeTarget(target)
            center.playCommand.removeTarget(target)
            center.pauseCommand.removeTarget(target)
        }
        commandTargets = []
        MPNowPlayingInfoCenter.default().playbackState = .stopped
    }

    private func startEventTap() {
        let mask = NSEvent.EventTypeMask.systemDefined
        let refcon = Unmanaged.passUnretained(self).toOpaque()

        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: CGEventMask(mask.rawValue),
            callback: { proxy, type, event, refcon -> Unmanaged<CGEvent>? in
                guard let refcon = refcon else {
                    return Unmanaged.passUnretained(event)
                }
                let obj = Unmanaged<MediaKeyInterceptor>.fromOpaque(refcon).takeUnretainedValue()
                return obj.handleEvent(proxy: proxy, type: type, event: event)
            },
            userInfo: refcon
        ) else {
            onStatusChange?(L("status.tap.failed"))
            return
        }

        eventTap = tap
        runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), runLoopSource, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        onStatusChange?(L("status.tap.started"))
    }

    private func startRemoteCommandBlock() {
        let center = MPRemoteCommandCenter.shared()

        center.togglePlayPauseCommand.isEnabled = true
        center.playCommand.isEnabled = true
        center.pauseCommand.isEnabled = true
        center.stopCommand.isEnabled = false
        center.nextTrackCommand.isEnabled = false
        center.previousTrackCommand.isEnabled = false

        let t1 = center.togglePlayPauseCommand.addTarget { _ in .success }
        let t2 = center.playCommand.addTarget { _ in .success }
        let t3 = center.pauseCommand.addTarget { _ in .success }
        commandTargets = [t1, t2, t3]

        MPNowPlayingInfoCenter.default().playbackState = .playing
        MPNowPlayingInfoCenter.default().nowPlayingInfo = [
            MPMediaItemPropertyTitle: L("title.app"),
            MPMediaItemPropertyArtist: "KeyRelay"
        ]
    }

    private func handleEvent(proxy: CGEventTapProxy, type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap = eventTap {
                CGEvent.tapEnable(tap: tap, enable: true)
            }
            return Unmanaged.passUnretained(event)
        }

        guard let nsEvent = NSEvent(cgEvent: event) else {
            return Unmanaged.passUnretained(event)
        }

        guard nsEvent.type == .systemDefined, nsEvent.subtype.rawValue == 8 else {
            return Unmanaged.passUnretained(event)
        }

        let data1 = nsEvent.data1
        let keyCode = (data1 & 0xFFFF0000) >> 16

        guard keyCode == 16 else {
            return Unmanaged.passUnretained(event)
        }

        let keyFlags = data1 & 0x0000FFFF
        let keyState = (keyFlags & 0xFF00) >> 8
        let isDown = (keyState == 0x0A)

        DispatchQueue.main.async { [weak self] in
            self?.onStatusChange?(isDown ? L("status.key.down") : L("status.key.up"))
        }

        simulateKey(down: isDown)
        return nil
    }

    func simulateKey(down: Bool) {
        let source = CGEventSource(stateID: .combinedSessionState)

        if isModifierKey {
            guard let flagEvent = CGEvent(source: source) else { return }
            flagEvent.type = .flagsChanged
            flagEvent.setIntegerValueField(.keyboardEventKeycode, value: Int64(targetKeyCode))
            flagEvent.flags = down ? modifierFlag : []
            flagEvent.post(tap: .cghidEventTap)
        } else {
            guard let keyEvent = CGEvent(keyboardEventSource: source, virtualKey: targetKeyCode, keyDown: down) else { return }
            keyEvent.post(tap: .cghidEventTap)
        }
    }
}
