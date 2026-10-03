import AppKit
import ApplicationServices
import Carbon

struct InputSourceState: Equatable {
    let id: String
    let chinese: Bool

    static func current() -> InputSourceState {
        guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue() else {
            return InputSourceState(id: "", chinese: false)
        }
        func property(_ key: CFString) -> AnyObject? {
            guard let pointer = TISGetInputSourceProperty(source, key) else { return nil }
            return Unmanaged<AnyObject>.fromOpaque(pointer).takeUnretainedValue()
        }
        let id = property(kTISPropertyInputSourceID) as? String ?? ""
        let languages = property(kTISPropertyInputSourceLanguages) as? [String] ?? []
        return InputSourceState(id: id, chinese: languages.contains { $0.hasPrefix("zh") })
    }
}

final class KeyboardMonitor {
    var onKey: ((CGEventType, UInt16, CGEventFlags, Bool) -> Void)?
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    var running: Bool { tap.map { CFMachPortIsValid($0) && CGEvent.tapIsEnabled(tap: $0) } ?? false }

    func start() {
        if tap != nil && !running { stop() }
        guard tap == nil, CGPreflightListenEventAccess() else { return }
        let mask = [CGEventType.keyDown, .keyUp, .flagsChanged].reduce(CGEventMask(0)) {
            $0 | (1 << $1.rawValue)
        }
        tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap,
                               options: .listenOnly, eventsOfInterest: mask, callback: { _, type, event, data in
            guard let data else { return Unmanaged.passUnretained(event) }
            let monitor = Unmanaged<KeyboardMonitor>.fromOpaque(data).takeUnretainedValue()
            if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
                if let tap = monitor.tap { CGEvent.tapEnable(tap: tap, enable: true) }
            } else {
                let code = UInt16(event.getIntegerValueField(.keyboardEventKeycode))
                let repeatKey = event.getIntegerValueField(.keyboardEventAutorepeat) != 0
                // Queue the work so AX queries and drawing never run inside the event tap.
                let flags = event.flags
                DispatchQueue.main.async { monitor.onKey?(type, code, flags, repeatKey) }
            }
            return Unmanaged.passUnretained(event)
        }, userInfo: Unmanaged.passUnretained(self).toOpaque())
        if let tap {
            source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
            CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
            CGEvent.tapEnable(tap: tap, enable: true)
        }
    }

    func stop() {
        if let source { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap { CFMachPortInvalidate(tap) }
        source = nil; tap = nil
    }

    deinit { stop() }

    // Physical ANSI positions, independent of the IME's composed text.
    static let letters: [UInt16: String] = [
        0:"a", 1:"s", 2:"d", 3:"f", 4:"h", 5:"g", 6:"z", 7:"x", 8:"c", 9:"v",
        11:"b", 12:"q", 13:"w", 14:"e", 15:"r", 16:"y", 17:"t", 31:"o", 32:"u",
        34:"i", 35:"p", 37:"l", 38:"j", 40:"k", 41:";", 45:"n", 46:"m"
    ]
}

/// Carbon hotkeys work even when Input Monitoring has not been granted.
final class GlobalHotKeys {
    var action: ((UInt32) -> Void)?
    private var handler: EventHandlerRef?
    private var hotkeys: [EventHotKeyRef] = []
    private(set) var available = true

    init() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let status = InstallEventHandler(GetApplicationEventTarget(), { _, event, data in
            guard let event, let data else { return OSStatus(eventNotHandledErr) }
            var id = EventHotKeyID()
            let result = GetEventParameter(event, EventParamName(kEventParamDirectObject),
                                           EventParamType(typeEventHotKeyID), nil,
                                           MemoryLayout<EventHotKeyID>.size, nil, &id)
            if result == noErr { Unmanaged<GlobalHotKeys>.fromOpaque(data).takeUnretainedValue().action?(id.id) }
            return result
        }, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), &handler)
        available = status == noErr
        for (index, key) in [UInt32(kVK_F8), UInt32(kVK_F9)].enumerated() {
            var reference: EventHotKeyRef?
            let id = EventHotKeyID(signature: 0x4B534857, id: UInt32(index + 1))
            let result = RegisterEventHotKey(key, UInt32(controlKey | optionKey), id,
                                             GetApplicationEventTarget(), 0, &reference)
            if let reference, result == noErr { hotkeys.append(reference) } else { available = false }
        }
    }

    deinit {
        hotkeys.forEach { UnregisterEventHotKey($0) }
        if let handler { RemoveEventHandler(handler) }
    }
}
