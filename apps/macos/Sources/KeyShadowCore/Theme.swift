import Foundation

public struct Theme: Decodable {
    public let id: String
    public let name: String
    public let colors: [String: String]
    public static let all: [Theme] = {
        let url = Resources.bundle.url(forResource: "themes", withExtension: "json")!
        return try! JSONDecoder().decode([Theme].self, from: Data(contentsOf: url))
    }()
    public static func named(_ id: String) -> Theme { all.first { $0.id == id } ?? all[0] }
}
