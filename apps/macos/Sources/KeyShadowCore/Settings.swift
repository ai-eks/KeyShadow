import Foundation
import CoreGraphics

public struct Settings: Codable {
    public var scheme = "flypy"
    public var theme = "navy"
    public var scale: Double = 1
    public var opacity: Double = 0.85
    public var compact = false
    /// Placement only: follow the input or stay at the saved position.
    public var followInput = true
    /// Visibility only: keep the keyboard on screen or show it while typing.
    public var alwaysShow = false
    public var hints = true
    public var delay: Double = 2
    public var x: Double?
    public var y: Double?

    public init() {}

    private enum CodingKeys: String, CodingKey {
        case scheme, theme, scale, opacity, compact, followInput, alwaysShow, hints, delay, x, y
    }

    // Settings saved by older versions lack newer keys; keep their values and default the rest.
    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        scheme = try values.decodeIfPresent(String.self, forKey: .scheme) ?? scheme
        theme = try values.decodeIfPresent(String.self, forKey: .theme) ?? theme
        scale = try values.decodeIfPresent(Double.self, forKey: .scale) ?? scale
        opacity = try values.decodeIfPresent(Double.self, forKey: .opacity) ?? opacity
        compact = try values.decodeIfPresent(Bool.self, forKey: .compact) ?? compact
        followInput = try values.decodeIfPresent(Bool.self, forKey: .followInput) ?? followInput
        alwaysShow = try values.decodeIfPresent(Bool.self, forKey: .alwaysShow) ?? alwaysShow
        hints = try values.decodeIfPresent(Bool.self, forKey: .hints) ?? hints
        delay = try values.decodeIfPresent(Double.self, forKey: .delay) ?? delay
        x = try values.decodeIfPresent(Double.self, forKey: .x)
        y = try values.decodeIfPresent(Double.self, forKey: .y)
    }

    public mutating func normalize() {
        scheme = PinyinData.bundled.scheme(scheme).id
        if !["navy", "graphite", "cream", "lavender"].contains(theme) { theme = "navy" }
        scale = scale.isFinite ? min(1.5, max(0.5, scale)) : 1
        opacity = opacity.isFinite ? min(0.95, max(0.35, opacity)) : 0.85
        if ![0, 1, 2, 3, 5, 10, 30].contains(delay) { delay = 2 }
        if x?.isFinite == false || y?.isFinite == false { x = nil; y = nil }
    }

    public static func load(from defaults: UserDefaults = .standard) -> Settings {
        guard let data = defaults.data(forKey: "settings"),
              var result = try? JSONDecoder().decode(Settings.self, from: data) else { return Settings() }
        result.normalize()
        return result
    }

    public func save(to defaults: UserDefaults = .standard) {
        if let data = try? JSONEncoder().encode(self) { defaults.set(data, forKey: "settings") }
    }
}

public enum InputPlacement {
    public static func clamp(_ origin: CGPoint, size: CGSize, screen: CGRect) -> CGPoint {
        CGPoint(x: max(screen.minX, min(origin.x, screen.maxX - size.width)),
                y: max(screen.minY, min(origin.y, screen.maxY - size.height)))
    }

    /// AppKit coordinates: y increases upwards. Prefer above the caret and candidate row.
    public static func place(anchor: CGRect, size: CGSize, screen: CGRect, gap: CGFloat = 72) -> CGPoint {
        let above = screen.maxY - anchor.maxY - gap
        let below = anchor.minY - screen.minY - gap
        let y = above >= size.height || above >= below ? anchor.maxY + gap : anchor.minY - gap - size.height
        return clamp(CGPoint(x: anchor.minX, y: y), size: size, screen: screen)
    }
}
