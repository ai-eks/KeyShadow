import XCTest
import CoreGraphics
@testable import KeyShadowCore

final class SchemeTests: XCTestCase {
    let data = PinyinData.bundled

    func testWindowsZeroInitialFixtures() {
        let syllables = "a ai an ang ao e ei en eng er o ou".split(separator: " ").map(String.init)
        let fixtures = [
            "flypy": "aa ai,ad an,aj ah ao,ac ee ei,ew en,ef eg er oo ou,oz",
            "natural": "aa ai,al an,aj ah ao,ak ee ei,ez en,ef eg er oo ou,ob",
            "microsoft": "oa,aa ol,al oj,aj oh,ah ok,ak oe,ee oz,ez of,ef og,eg or,er oo ob,ou",
            "abc": "oa ol oj oh ok oe oq of og or oo ob"
        ]
        for (id, values) in fixtures {
            for (syllable, codes) in zip(syllables, values.split(separator: " ")) {
                XCTAssertEqual(Set(data.scheme(id).codes(for: syllable)), Set(codes.split(separator: ",").map(String.init)), "\(id) \(syllable)")
            }
        }
    }

    func testEverySyllableAndKeyPair() {
        XCTAssertEqual(data.schemes.count, 4)
        XCTAssertEqual(data.syllables.count, 411)
        XCTAssertEqual(Set(data.syllables).count, 411)
        for scheme in data.schemes {
            let guide = PinyinGuide(scheme: scheme)
            XCTAssertEqual(guide.decode("lo"), "lo/luo")
            XCTAssertEqual(guide.decode("invalid"), "")
            XCTAssertEqual(scheme.usesSemicolon, scheme.id == "microsoft")
            for syllable in data.syllables {
                let codes = scheme.codes(for: syllable)
                XCTAssertFalse(codes.isEmpty, "\(scheme.id) \(syllable)")
                XCTAssertEqual(codes.count, Set(codes).count)
                for code in codes {
                    XCTAssertEqual(code.count, 2)
                    XCTAssertTrue(guide.decode(code).split(separator: "/").contains(Substring(syllable.replacingOccurrences(of: "v", with: "ü"))))
                    XCTAssertFalse(scheme.finalLabel(String(code.suffix(1))).isEmpty)
                }
            }
            for first in "abcdefghijklmnopqrstuvwxyz;" {
                for second in "abcdefghijklmnopqrstuvwxyz;" {
                    let a = String(first), b = String(second)
                    XCTAssertEqual(guide.canFollow(a, b), !guide.decode(a + b).isEmpty)
                }
            }
        }
        XCTAssertEqual(data.scheme("unknown").id, "flypy")
    }

    func testWindowsSpecialSpellingFixtures() {
        for id in ["flypy", "natural", "microsoft", "abc"] {
            let scheme = data.scheme(id)
            let microsoft = id == "microsoft", abc = id == "abc"
            XCTAssertEqual(scheme.codes(for: "shuang"), [id == "flypy" ? "ul" : abc ? "vt" : "ud"])
            XCTAssertEqual(scheme.codes(for: "bing"), [microsoft ? "b;" : id == "flypy" ? "bk" : "by"])
            for initial in ["n", "l"] {
                XCTAssertEqual(scheme.codes(for: initial + "ü"), [initial + (microsoft ? "y" : "v")])
                XCTAssertEqual(Set(scheme.codes(for: initial + "üe")), Set(microsoft ? [initial + "t", initial + "v"] : [initial + (abc ? "m" : "t")]))
            }
            for initial in ["j", "q", "x", "y"] {
                XCTAssertEqual(Set(scheme.codes(for: initial + "u")), Set([initial + "u", initial + (microsoft ? "y" : "v")]))
                XCTAssertEqual(Set(scheme.codes(for: initial + "ue")), Set(microsoft ? [initial + "t", initial + "v"] : [initial + (abc ? "m" : "t")]))
            }
            XCTAssertEqual(scheme.initialLabel(abc ? "a" : "v"), "zh")
        }
        XCTAssertEqual(data.scheme("microsoft").finalLabel(";"), "ing")
        XCTAssertEqual(data.scheme("microsoft").finalLabel("v"), "ui/ue/üe")
        XCTAssertEqual(data.scheme("abc").codes(for: "zhong"), ["as"])
        XCTAssertEqual(data.scheme("abc").codes(for: "cheng"), ["eg"])
    }
}

final class InputSessionTests: XCTestCase {
    func testPausePreservesFirstKeyAndBackspaceCorrectsSecond() {
        var state = InputSession()
        state.press("u", at: 10)
        XCTAssertTrue(state.visible(at: 11))
        XCTAssertFalse(state.visible(at: 12))
        XCTAssertEqual(state.pending, "u")
        state.press("l", at: 13)
        XCTAssertEqual(state.pair, "ul")
        state.press("backspace", at: 14)
        XCTAssertEqual(state.pending, "u")
        state.press("i", at: 15)
        XCTAssertEqual(state.pair, "ui")
        state.press("space", at: 16)
        XCTAssertEqual(state.pair, "")
        XCTAssertFalse(state.visible(at: 16))
    }

    func testCloseRequiresExplicitReveal() {
        var state = InputSession()
        state.close(); state.press("n", at: 1)
        XCTAssertFalse(state.visible(at: 1))
        XCTAssertEqual(state.pending, "")
        state.reset(); state.press("n", at: 2)
        XCTAssertTrue(state.manuallyHidden)
        state.preview(at: 3)
        XCTAssertTrue(state.visible(at: 6))
        XCTAssertFalse(state.visible(at: 7))
        state.press("n", at: 8)
        XCTAssertTrue(state.visible(at: 8))
    }

    func testCollapseClearsPairAndNextInputResumes() {
        var state = InputSession()
        state.press("u", at: 1); state.press("l", at: 2)
        state.reset()
        XCTAssertFalse(state.visible(at: 3))
        XCTAssertEqual(state.pair, "")
        state.press("n", at: 4)
        XCTAssertTrue(state.visible(at: 4))
        XCTAssertEqual(state.pending, "n")
    }

    func testCandidateSelectionEndsTyping() {
        var state = InputSession()
        state.press("u", at: 1); state.press("l", at: 2)
        state.press("space", at: 3)
        XCTAssertFalse(state.visible(at: 3))
        XCTAssertEqual(state.pending, "")
        XCTAssertEqual(state.pair, "")
        state.press("n", at: 4)
        XCTAssertEqual(state.pending, "n")
    }

    func testBackspaceAloneAndContextChangeDoNotShow() {
        var state = InputSession()
        state.press("backspace", at: 1)
        XCTAssertFalse(state.visible(at: 1))
        state.press("n", at: 2); state.reset()
        XCTAssertFalse(state.visible(at: 2))
        XCTAssertEqual(state.pending, "")
        state.press("v", at: 3)
        XCTAssertEqual(state.pending, "v")
    }

    func testNoIdleHideStillClearsOnCommitAndContextChange() {
        var state = InputSession(); state.delay = 0
        state.press("n", at: 1)
        XCTAssertTrue(state.visible(at: 1_000))
        state.press("escape", at: 1_001)
        XCTAssertFalse(state.visible(at: 1_001))
        state.preview(at: 2_000)
        XCTAssertFalse(state.visible(at: 2_004))
    }

    func testMenuPauseAndDelayChanges() {
        var state = InputSession()
        state.press("u", at: 1)
        XCTAssertTrue(state.visible(at: 10, paused: true))
        state.refreshDeadline(at: 10)
        XCTAssertTrue(state.visible(at: 11))
        XCTAssertFalse(state.visible(at: 12))
        state.delay = 30; state.refreshDeadline(at: 20)
        XCTAssertFalse(state.visible(at: 20), "Changing delay does not begin typing")
    }
}

final class SettingsAndPlacementTests: XCTestCase {
    func testSettingsRoundtripAndClamps() throws {
        let name = "KeyShadowTests." + UUID().uuidString
        let defaults = try XCTUnwrap(UserDefaults(suiteName: name))
        defer { defaults.removePersistentDomain(forName: name) }
        var state = Settings()
        state.scale = 4; state.opacity = 0; state.scheme = "obsolete"; state.theme = "unknown"
        state.delay = -2; state.normalize()
        XCTAssertEqual(state.scale, 1.5); XCTAssertEqual(state.opacity, 0.35)
        XCTAssertEqual(state.scheme, "flypy"); XCTAssertEqual(state.theme, "navy"); XCTAssertEqual(state.delay, 2)
        XCTAssertFalse(state.alwaysShow); XCTAssertTrue(state.hints)
        state.compact = true; state.followInput = false; state.alwaysShow = true; state.hints = false
        state.x = -1200; state.save(to: defaults)
        let loaded = Settings.load(from: defaults)
        XCTAssertTrue(loaded.compact); XCTAssertFalse(loaded.followInput); XCTAssertEqual(loaded.x, -1200)
        XCTAssertTrue(loaded.alwaysShow); XCTAssertFalse(loaded.hints)
        defaults.set(Data("bad data".utf8), forKey: "settings")
        XCTAssertEqual(Settings.load(from: defaults).scale, 1)
    }

    func testOlderSettingsKeepValuesAndDefaultNewKeys() throws {
        let name = "KeyShadowTests." + UUID().uuidString
        let defaults = try XCTUnwrap(UserDefaults(suiteName: name))
        defer { defaults.removePersistentDomain(forName: name) }
        let saved = #"{"scheme":"abc","theme":"cream","scale":0.75,"opacity":0.5,"compact":true,"followInput":false,"hints":false,"delay":5,"x":12,"y":34}"#
        defaults.set(Data(saved.utf8), forKey: "settings")
        let loaded = Settings.load(from: defaults)
        XCTAssertEqual(loaded.scheme, "abc"); XCTAssertEqual(loaded.theme, "cream"); XCTAssertEqual(loaded.scale, 0.75)
        XCTAssertFalse(loaded.followInput); XCTAssertFalse(loaded.hints); XCTAssertEqual(loaded.delay, 5)
        XCTAssertEqual(loaded.x, 12); XCTAssertEqual(loaded.y, 34)
        XCTAssertFalse(loaded.alwaysShow, "A missing display mode shows the keyboard only while typing")
    }

    func testPlacementOnEitherSideAndNegativeOriginScreens() {
        let screen = CGRect(x: -1600, y: -200, width: 1600, height: 1000)
        let size = CGSize(width: 820, height: 344)
        for anchor in [CGRect(x: -1400, y: 0, width: 1, height: 20), CGRect(x: -10, y: 770, width: 1, height: 20)] {
            let point = InputPlacement.place(anchor: anchor, size: size, screen: screen)
            XCTAssertTrue(screen.contains(CGRect(origin: point, size: size)))
            XCTAssertFalse(CGRect(origin: point, size: size).intersects(anchor))
        }
        let above = InputPlacement.place(anchor: CGRect(x: -1400, y: 0, width: 1, height: 20), size: size, screen: screen)
        XCTAssertEqual(above.y, 92, "Both platforms reserve 72 points for the candidate row")
    }
}
