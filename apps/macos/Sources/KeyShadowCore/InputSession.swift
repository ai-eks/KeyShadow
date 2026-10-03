import Foundation

/// Ephemeral two-key state only. No text, candidates, or typing history.
public struct InputSession {
    public private(set) var pending = ""
    public private(set) var pair = ""
    public private(set) var manuallyHidden = false
    public private(set) var typing = false
    public private(set) var previewUntil: TimeInterval = 0
    private var lastInput: TimeInterval = 0
    public var delay: TimeInterval = 2

    public init() {}

    public mutating func reset() {
        pending = ""; pair = ""; typing = false; previewUntil = 0
    }

    public mutating func close() { reset(); manuallyHidden = true }

    public mutating func resume() { manuallyHidden = false }

    public mutating func preview(at now: TimeInterval) {
        reset(); manuallyHidden = false; previewUntil = now + 4
    }

    public mutating func press(_ key: String, at now: TimeInterval) {
        guard !manuallyHidden else { return }
        if key == "backspace" {
            guard typing else { return }
            if !pair.isEmpty { pending = String(pair.prefix(1)); pair = "" }
            else { pending = "" }
        } else if key.count == 1 && "abcdefghijklmnopqrstuvwxyz;".contains(key) {
            if pending.isEmpty { pending = key; pair = "" }
            else { pair = pending + key; pending = "" }
        } else {
            reset(); return
        }
        typing = true; lastInput = now; previewUntil = 0
    }

    public mutating func refreshDeadline(at now: TimeInterval) { if typing { lastInput = now } }

    public mutating func visible(at now: TimeInterval, paused: Bool = false) -> Bool {
        guard !manuallyHidden else { return false }
        if paused { return typing || previewUntil > 0 }
        if typing && delay > 0 && now - lastInput >= delay { typing = false }
        if previewUntil > 0 && now >= previewUntil { previewUntil = 0 }
        return typing || previewUntil > now
    }
}
