import AppKit
import ApplicationServices
import Carbon

struct InputTarget {
    let element: AXUIElement
    let caret: CGRect?
    let field: CGRect?

    init(element: AXUIElement, caret: CGRect?, field: CGRect? = nil) {
        self.element = element; self.caret = caret; self.field = field
    }
}

enum InputInspection {
    case allowed(InputTarget)
    case blocked
    case unknown
}

protocol InputInspecting {
    func inspect(pid: pid_t, includeCaret: Bool, completion: @escaping (InputInspection) -> Void)
}

final class InputInspector: InputInspecting {
    private let queue = DispatchQueue(label: "uy.aix.keyshadow.input", qos: .userInitiated)
    static let timeout: TimeInterval = 0.65

    func inspect(pid: pid_t, includeCaret: Bool, completion: @escaping (InputInspection) -> Void) {
        let started = ProcessInfo.processInfo.systemUptime
        // Serialize checks so fast first/second keys are applied in order. Expired
        // queued work is discarded rather than replaying input after a slow provider.
        queue.async {
            let target = ProcessInfo.processInfo.systemUptime - started < Self.timeout
                ? Self.read(pid: pid, includeCaret: includeCaret) : .unknown
            DispatchQueue.main.async {
                completion(ProcessInfo.processInfo.systemUptime - started < Self.timeout ? target : .unknown)
            }
        }
    }

    static func read(pid: pid_t, includeCaret: Bool, messagingTimeout: Float = 0.1) -> InputInspection {
        guard !IsSecureEventInputEnabled() else { return .blocked }
        guard pid > 0, AXIsProcessTrusted() else { return .unknown }
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, messagingTimeout)
        guard let element = focusedElement(app) else { return .unknown }
        AXUIElementSetMessagingTimeout(element, messagingTimeout)
        guard let allowed = permitsHints(element) else { return .unknown }
        guard allowed else { return .blocked }
        let rect = includeCaret ? caret(element) : nil
        let field = includeCaret && rect == nil ? fieldBounds(element) : nil
        // Browser fields share a process/window. Verify the actual focused element
        // again after reading metadata/geometry, including a changed password type.
        guard !IsSecureEventInputEnabled() else { return .blocked }
        guard let latest = focusedElement(app), CFEqual(element, latest),
              let stillAllowed = permitsHints(element) else { return .unknown }
        guard stillAllowed else { return .blocked }
        return .allowed(InputTarget(element: element, caret: rect, field: field))
    }

    private static func focusedElement(_ app: AXUIElement) -> AXUIElement? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXFocusedUIElementAttribute as CFString, &value) == .success,
              let value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }

    static func permitsHints(_ element: AXUIElement) -> Bool? {
        var role: CFTypeRef?, subrole: CFTypeRef?, focused: CFTypeRef?
        let roleStatus = AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &role)
        let status = AXUIElementCopyAttributeValue(element, kAXSubroleAttribute as CFString, &subrole)
        if status == .success && subrole as? String == kAXSecureTextFieldSubrole { return false }
        // Subrole is optional: ordinary AppKit text fields return attributeUnsupported.
        // A provider error is different from a supported text role with no subrole.
        guard status == .attributeUnsupported || status == .noValue
                || (status == .success && subrole is String)
        else { return nil }
        guard roleStatus == .success, let role = role as? String,
              [kAXTextFieldRole, kAXTextAreaRole, kAXComboBoxRole].contains(role) else { return nil }
        guard AXUIElementCopyAttributeValue(element, kAXFocusedAttribute as CFString, &focused) == .success,
              let isFocused = focused as? Bool else { return nil }
        guard isFocused else { return false }
        var writable = DarwinBoolean(false)
        // Ask whether AXValue is writable; never read AXValue, selected text or labels.
        guard AXUIElementIsAttributeSettable(element, kAXValueAttribute as CFString, &writable) == .success else { return nil }
        return writable.boolValue
    }

    private static func caret(_ element: AXUIElement) -> CGRect? {
        var selection: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &selection) == .success,
              let selection, CFGetTypeID(selection) == AXValueGetTypeID() else { return nil }
        let value = selection as! AXValue
        var range = CFRange()
        guard AXValueGetType(value) == .cfRange, AXValueGetValue(value, .cfRange, &range) else { return nil }
        range.length = 0
        guard let parameter = AXValueCreate(.cfRange, &range) else { return nil }
        var bounds: CFTypeRef?
        guard AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute as CFString,
                                                         parameter, &bounds) == .success,
              let bounds, CFGetTypeID(bounds) == AXValueGetTypeID() else { return nil }
        let rectangle = bounds as! AXValue
        var rect = CGRect.zero
        guard AXValueGetType(rectangle) == .cgRect, AXValueGetValue(rectangle, .cgRect, &rect),
              rect.origin.x.isFinite, rect.origin.y.isFinite, rect.width.isFinite,
              rect.height.isFinite, rect.height > 0 else { return nil }
        return rect
    }

    private static func fieldBounds(_ element: AXUIElement) -> CGRect? {
        var position: CFTypeRef?, size: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXPositionAttribute as CFString, &position) == .success,
              AXUIElementCopyAttributeValue(element, kAXSizeAttribute as CFString, &size) == .success,
              let position, let size, CFGetTypeID(position) == AXValueGetTypeID(),
              CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
        let pointValue = position as! AXValue, sizeValue = size as! AXValue
        var point = CGPoint.zero, dimensions = CGSize.zero
        guard AXValueGetType(pointValue) == .cgPoint, AXValueGetValue(pointValue, .cgPoint, &point),
              AXValueGetType(sizeValue) == .cgSize, AXValueGetValue(sizeValue, .cgSize, &dimensions),
              point.x.isFinite, point.y.isFinite, dimensions.width.isFinite, dimensions.height.isFinite,
              dimensions.width > 0, dimensions.height > 0 else { return nil }
        return CGRect(origin: point, size: dimensions)
    }
}
