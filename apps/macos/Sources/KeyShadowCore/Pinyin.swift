import Foundation

enum Resources {
    // SwiftPM tests use Bundle.module; the standalone app keeps resources in Contents/Resources.
    static let bundle: Bundle = Bundle.main.resourceURL
        .flatMap { Bundle(url: $0.appendingPathComponent("KeyShadow_KeyShadowCore.bundle")) } ?? .module
}

public struct PinyinData: Decodable {
    public let schemes: [PinyinScheme]
    public let syllables: [String]

    public static let bundled: PinyinData = {
        // Bundled build-time data, checked against the Windows source in CI.
        let url = Resources.bundle.url(forResource: "windows-pinyin", withExtension: "json")!
        return try! JSONDecoder().decode(PinyinData.self, from: Data(contentsOf: url))
    }()

    public func scheme(_ id: String) -> PinyinScheme {
        schemes.first { $0.id == id } ?? schemes[0]
    }
}

public struct PinyinScheme: Decodable {
    public let id: String
    public let name: String
    public let initials: String
    public let zero: String
    public let layout: String
    public let hints: [String]

    private var entries: [(String, [String])] {
        layout.split(separator: " ").map {
            let parts = $0.split(separator: ":")
            return (String(parts[0]), parts[1].split(separator: "/").map(String.init))
        }
    }

    public var usesSemicolon: Bool { layout.contains(";:") }

    public func finalLabel(_ key: String) -> String {
        (entries.first { $0.0 == key }?.1 ?? []).joined(separator: "/")
            .replacingOccurrences(of: "v", with: "ü")
    }

    public func initialLabel(_ key: String) -> String {
        if let index = initials.map(String.init).firstIndex(of: key) {
            return ["zh", "ch", "sh"][index]
        }
        return zero != "Preserve" && key == "o" ? "零" : ""
    }

    public func codes(for spelling: String) -> [String] {
        let syllable = spelling.replacingOccurrences(of: "ü", with: "v")
        guard let first = syllable.first else { return [] }
        var result: [String] = []
        func add(_ initial: String, _ final: String) {
            for (key, finals) in entries where finals.contains(final) {
                let code = initial + key
                if !result.contains(code) { result.append(code) }
            }
        }
        if "aeo".contains(first) {
            if zero == "Preserve" {
                if syllable.count == 2 { result.append(syllable) }
                add(String(first), syllable)
            } else {
                add("o", syllable)
                if zero == "PrefixOAndVowel" {
                    if "ae".contains(first) { add(String(first), syllable) }
                    if syllable == "ou" { result.append("ou") }
                }
            }
        } else {
            let compound = ["zh", "ch", "sh"].firstIndex { syllable.hasPrefix($0) }
            let initial = compound.map { initials.map(String.init)[$0] } ?? String(first)
            let final = String(syllable.dropFirst(compound == nil ? 1 : 2))
            add(initial, final)
            if final == "u" && "jqxy".contains(first) { add(initial, "v") }
        }
        return result
    }
}

public struct PinyinGuide {
    public let scheme: PinyinScheme
    private var lookup: [String: String] = [:]

    public init(scheme: PinyinScheme) {
        self.scheme = scheme
        for syllable in PinyinData.bundled.syllables {
            for code in scheme.codes(for: syllable) {
                let value = syllable.replacingOccurrences(of: "v", with: "ü")
                lookup[code] = lookup[code].map { $0 + "/" + value } ?? value
            }
        }
    }

    public func decode(_ code: String) -> String { lookup[code] ?? "" }
    public func canFollow(_ first: String, _ second: String) -> Bool { lookup[first + second] != nil }
    public func initialLabel(_ key: String) -> String {
        let label = scheme.initialLabel(key)
        if label == "零" { return "零声母" }
        if !label.isEmpty { return label }
        return ["a", "e", "o"].contains(key) ? key + "（零声母）" : key
    }
}
