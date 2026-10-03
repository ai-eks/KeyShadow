import AppKit
import Carbon
import KeyShadowCore

/// Controlled privacy results exercise the same asynchronous path as the AX provider.
final class TestInputInspector: InputInspecting {
    var result: InputInspection = .allowed(InputTarget(element: AXUIElementCreateApplication(1),
                                                     caret: CGRect(x: 200, y: 400, width: 1, height: 20)))
    var deferred = false
    var inspectionCount = 0
    var completions: [(InputInspection) -> Void] = []
    func inspect(pid: pid_t, includeCaret: Bool, completion: @escaping (InputInspection) -> Void) {
        inspectionCount += 1
        if deferred { completions.append(completion) } else { completion(result) }
    }
    func finish(_ result: InputInspection) { completions.removeFirst()(result) }
}

extension AppDelegate {
    func runPrivacyTests(_ provider: TestInputInspector) throws {
        var checks = 0
        func check(_ condition: @autoclosure () -> Bool, _ label: String) throws {
            checks += 1
            if !condition() { throw NSError(domain: "PrivacyTests", code: checks, userInfo: [NSLocalizedDescriptionKey: label]) }
        }
        guard case .allowed(let normal) = provider.result else { preconditionFailure("Expected a normal test input") }
        let other = InputTarget(element: AXUIElementCreateApplication(2), caret: normal.caret)
        for follow in [false, true] {
          for always in [false, true] {
            for compact in [false, true] {
                settings.followInput = follow; settings.alwaysShow = always; settings.compact = compact; applySettings()
                provider.result = .allowed(normal); reveal()
                let origin = panel.frame.origin
                processKey(.keyDown, 32, [], false, chinese: true)
                try check(panel.isVisible && session.pending == "u", "first Chinese key")
                perform("hide")
                try check(!panel.isVisible && session.pending.isEmpty && flashes.isEmpty && !session.manuallyHidden, "collapse clears transient input")
                processKey(.keyDown, 37, [], false, chinese: true)
                try check(panel.isVisible && session.pending == "l" && session.pair.isEmpty, "collapse resumes on a fresh first key")
                if !follow { try check(panel.frame.origin == origin, "fixed collapse preserves position") }
                perform("close")
                processKey(.keyDown, 0, [], false, chinese: true)
                try check(!panel.isVisible && session.manuallyHidden && session.pending.isEmpty && flashes.isEmpty, "close suspends typing")
                perform("follow"); perform("follow")
                try check(session.manuallyHidden, "placement changes cannot resume a closed keyboard")
                perform("toggle")
                try check(panel.isVisible && !session.manuallyHidden && settings.compact == compact, "shortcut resumes same layout")
                perform("toggle")
                try check(!panel.isVisible && session.manuallyHidden, "shortcut closes visible keyboard")
                reveal()

                provider.result = .blocked // A known password or read-only field.
                processKey(.keyDown, 0, [], false, chinese: true)
                try check(panel.isVisible == always && session.pending.isEmpty && session.pair.isEmpty && flashes.isEmpty,
                          "rejected target clears all hints")
                reveal()
                try check(panel.isVisible == always && session.previewUntil == 0, "manual preview cannot bypass privacy")
                provider.result = .unknown
                reveal(); processKey(.keyDown, 32, [], false, chinese: true)
                try check(panel.isVisible && keyboard.session.pending == "u" && privacyAllowed,
                          "unknown metadata defaults to highlights in every layout and position mode")
                let fallback = panel.frame.origin
                processKey(.keyDown, 37, [], false, chinese: true)
                try check(panel.isVisible && keyboard.session.pair == "ul" && panel.frame.origin == fallback,
                          "unknown metadata keeps continuous typing visible at the fallback position")
                requestInputCheck()
                try check(panel.isVisible && keyboard.session.pair == "ul", "unknown background checks preserve highlights")
                perform("close")
                processKey(.keyDown, 32, [], false, chinese: true)
                try check(!panel.isVisible && session.manuallyHidden, "unknown input respects the user's closed state")
                reveal()
                provider.result = .allowed(normal)
                processKey(.keyDown, 32, [], false, chinese: true)
                try check(panel.isVisible && session.pending == "u", "normal input recovers after password")
                provider.deferred = true
                requestInputCheck()
                try check(panel.isVisible, "background privacy recheck keeps an approved field visible")
                provider.finish(.blocked)
                try check(panel.isVisible == always && session.pending.isEmpty, "background rejection clears the old field")
                provider.deferred = false
                reveal()
                processKey(.keyDown, 32, [], false, chinese: true)

                provider.deferred = true
                processKey(.keyDown, 37, [], false, chinese: true)
                try check(panel.isVisible && keyboard.session.pending == "u",
                          "unavailable metadata keeps existing highlights while the next check is pending")
                provider.finish(.blocked)
                try check(panel.isVisible == always && session.pending.isEmpty && flashes.isEmpty,
                          "asynchronous rejection clears old normal input")
                processKey(.keyDown, 0, [], false, chinese: true)
                processKey(.keyDown, 45, [], false, chinese: true)
                provider.finish(.blocked)
                try check(panel.isVisible == always && session.pending.isEmpty, "older password result stays suppressed")
                provider.finish(.allowed(normal))
                try check(panel.isVisible && session.pending == "n", "new field keeps its first key after an older rejection")
                perform("hide")
                processKey(.keyDown, 32, [], false, chinese: true)
                processKey(.keyDown, 37, [], false, chinese: true)
                provider.finish(.allowed(normal))
                renderVisibility()
                try check(panel.isVisible == always && session.pending == "u" && keyboard.session.pending.isEmpty,
                          "first result stays suppressed while second is pending")
                provider.finish(.allowed(normal))
                try check(panel.isVisible && session.pair == "ul", "rapid keys preserve their order")
                processKey(.keyDown, 37, [], true, chinese: true)
                provider.finish(.allowed(normal))
                try check(session.pair == "ul", "repeats never advance an approved pair")
                processKey(.keyDown, 0, [], false, chinese: true)
                provider.finish(.allowed(other))
                try check(session.pending == "a" && session.pair.isEmpty, "new field discards the previous field's pair")

                for command in ["hide", "close", "follow"] {
                    processKey(.keyDown, 32, [], false, chinese: true)
                    perform(command)
                    let after = panel.frame.origin
                    provider.finish(.allowed(normal))
                    renderVisibility()
                    try check(panel.isVisible == (command == "follow" && always) && session.pending.isEmpty &&
                              panel.frame.origin == after, "stale result after \(command) cannot show or reposition")
                    if command == "follow" { perform("follow") }
                    provider.deferred = false; reveal(); provider.deferred = true
                }
                processKey(.keyDown, 32, [], false, chinese: true)
                let realClock = uptime
                let expired = now + 1
                uptime = { expired }
                provider.finish(.allowed(normal))
                try check(panel.isVisible && session.pending == "u" && keyboard.session.pending == "u",
                          "late provider result defaults to highlights")
                uptime = realClock
                provider.deferred = false
                reveal()
                secureInputEnabled = { true }
                renderVisibility()
                try check(panel.isVisible == always && keyboard.session.pending.isEmpty && keyboard.flashes.isEmpty,
                          "secure input suppresses hints even in an approved preview")
                reveal()
                try check(panel.isVisible == always && session.previewUntil == 0,
                          "secure input blocks manual preview hints")
                secureInputEnabled = { false }
            }
          }
        }
        provider.result = .allowed(normal)
        for follow in [false, true] {
            for always in [false, true] {
                settings.followInput = follow; settings.alwaysShow = always; applySettings()
                // Space, digit, punctuation, Return, arrow and Tab all end the current input.
                for code in [kVK_Space, kVK_ANSI_1, kVK_ANSI_Comma, kVK_Return, kVK_LeftArrow, kVK_Tab] {
                    reveal()
                    processKey(.keyDown, 32, [], false, chinese: true)
                    processKey(.keyDown, 37, [], false, chinese: true)
                    try check(panel.isVisible && session.pair == "ul", "typing shows the pair before key \(code)")
                    processKey(.keyDown, UInt16(code), [], false, chinese: true)
                    renderVisibility()
                    try check(panel.isVisible == always && !session.typing && session.pending.isEmpty &&
                              session.pair.isEmpty && keyboard.session.pair.isEmpty,
                              "non-letter key \(code) ends input: follow=\(follow) always=\(always)")
                }
                processKey(.keyDown, 32, [], false, chinese: true)
                processKey(.keyDown, 51, [], false, chinese: true)
                try check(panel.isVisible && session.typing && session.pending.isEmpty,
                          "backspace corrects the pair without ending input")
                processKey(.flagsChanged, 56, [.maskShift], false, chinese: true)
                try check(panel.isVisible && session.typing, "a modifier alone does not end input")
            }
        }
        settings.followInput = false; settings.alwaysShow = true; applySettings()
        reveal(); renderVisibility()
        provider.result = .blocked
        processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible && session.pending.isEmpty && !privacyAllowed,
                  "always-show keeps only the static keyboard when the field becomes blocked")
        settings.alwaysShow = false; applySettings()
        provider.result = .allowed(normal)
        reveal()
        provider.result = .allowed(InputTarget(element: normal.element, caret: nil))
        processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible, "fixed position requires privacy but not caret geometry")
        perform("follow")
        panel.ignoresMouseEvents = true
        provider.result = .allowed(normal)
        processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible, "automatic position follows an available caret")
        let caretOrigin = panel.frame.origin
        restorePosition()
        let fallbackOrigin = panel.frame.origin
        panel.setFrameOrigin(caretOrigin)
        provider.result = .allowed(InputTarget(element: normal.element, caret: nil,
                                               field: CGRect(x: 1100, y: 100, width: 200, height: 40)))
        processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible && panel.frame.origin != fallbackOrigin,
                  "automatic position follows a verified field when caret geometry is unavailable")
        panel.setFrameOrigin(caretOrigin)
        provider.result = .allowed(InputTarget(element: normal.element, caret: nil))
        resetContext()
        processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible && panel.frame.origin == fallbackOrigin,
                  "automatic position uses the saved fallback when no geometry is available")
        panel.ignoresMouseEvents = false
        provider.result = .allowed(normal)
        resetContext()
        perform("close")
        let menu = makeMenu()
        menuWillOpen(menu)
        foregroundPID = { ProcessInfo.processInfo.processIdentifier }
        try check((menu.items[0].representedObject as? String) == "reveal", "closed state offers recovery")
        panel.orderFrontRegardless() // Visibility can change while the menu is open.
        menuAction(menu.items[0])
        try check(!session.manuallyHidden && pendingMenuReveal, "menu recovery records the user's choice immediately")
        menuDidClose(menu)
        try check(pendingMenuReveal, "menu recovery waits for an external input app")
        foregroundPID = { 1234 }
        tick()
        try check(panel.isVisible && !session.manuallyHidden, "menu recovery waits until the previous input app regains focus")
        resetContext()
        try check((makeMenu().items[0].representedObject as? String) == "close", "temporary invisibility preserves enabled state")
        perform("toggle")
        try check(session.manuallyHidden, "shortcut closes the enabled state even when the panel is hidden")
        perform("toggle")
        try check(!session.manuallyHidden && panel.isVisible, "shortcut restores the disabled state")
        resetContext()
        for follow in [false, true] {
            settings.followInput = follow; settings.alwaysShow = true; applySettings()
            provider.result = .blocked
            perform("close")
            reveal(); renderVisibility()
            try check(panel.isVisible && !session.manuallyHidden && !privacyAllowed,
                      "always-show stays visible in a blocked input field: follow=\(follow)")
            processKey(.keyDown, 32, [], false, chinese: true)
            renderVisibility()
            try check(panel.isVisible && session.pending.isEmpty && flashes.isEmpty,
                      "blocked input leaves only the static keyboard visible: follow=\(follow)")
            perform("hide"); renderVisibility()
            try check(!panel.isVisible, "temporary collapse still hides an always-shown keyboard: follow=\(follow)")
            provider.result = .allowed(normal)
            processKey(.keyDown, 32, [], false, chinese: true)
            renderVisibility()
            try check(panel.isVisible && session.pending == "u", "next safe key restores a collapsed always-shown keyboard")
            let previousDelay = session.delay, realClock = uptime
            session.delay = 1
            let later = now + 2
            uptime = { later }; renderVisibility()
            try check(panel.isVisible && session.pending == "u" && keyboard.session.pending.isEmpty,
                      "idle always-show retains the pair but shows only the static keyboard: follow=\(follow)")
            uptime = realClock; session.delay = previousDelay
        }
        settings.followInput = false; settings.alwaysShow = false; applySettings()
        resetContext()
        try check(!panel.isVisible, "fixed position alone does not keep the keyboard on screen")
        settings.followInput = true; applySettings()
        provider.result = .allowed(normal)
        let inspections = provider.inspectionCount
        panel.ignoresMouseEvents = true
        reveal(); processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible && privacyAllowed && provider.inspectionCount > inspections,
                  "every app inspects the input when Accessibility is available")
        provider.result = .unknown
        reveal(); processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible && keyboard.session.pending == "u",
                  "unknown input in any app defaults to key highlights")
        resetContext(); provider.deferred = true
        processKey(.keyDown, 32, [], false, chinese: true)
        let deadline = Date(timeIntervalSinceNow: 1)
        while pendingChecks > 0 && Date() < deadline { RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.01)) }
        try check(pendingChecks == 0 && panel.isVisible && keyboard.session.pending == "u",
                  "a stalled provider defaults to highlights at the check deadline")
        provider.finish(.blocked)
        try check(panel.isVisible && keyboard.session.pending == "u",
                  "a result arriving after fallback cannot erase the current key")
        provider.deferred = false; provider.result = .blocked
        let noPermissionInspections = provider.inspectionCount
        accessibilityTrusted = { false }
        for follow in [false, true] {
            settings.followInput = follow; settings.alwaysShow = !follow; applySettings(); reveal()
            processKey(.keyDown, 32, [], false, chinese: true)
            processKey(.keyDown, 37, [], false, chinese: true)
            try check(panel.isVisible && keyboard.session.pair == "ul" &&
                      provider.inspectionCount == noPermissionInspections,
                      "without Accessibility all apps highlight keys without inspecting the input")
            perform("close")
            processKey(.keyDown, 32, [], false, chinese: true)
            try check(!panel.isVisible && session.manuallyHidden, "no-permission fallback respects manual close")
            reveal(); processKey(.keyDown, 32, [], false, chinese: true)
            secureInputEnabled = { true }; renderVisibility()
            try check(panel.isVisible == !follow && keyboard.session.pending.isEmpty,
                      "system secure input blocks highlights without Accessibility")
            secureInputEnabled = { false }
        }
        settings.alwaysShow = false; applySettings()
        accessibilityTrusted = { true }; requestInputCheck()
        try check(!panel.isVisible && !privacyAllowed && provider.inspectionCount > noPermissionInspections,
                  "restoring Accessibility blocks an identified password or read-only field")

        provider.result = .unknown
        settings.followInput = true
        panel.ignoresMouseEvents = true
        reveal()
        let primaryTop = NSScreen.screens[0].frame.maxY
        for trusted in [false, true] {
            accessibilityTrusted = { trusted }
            for display in NSScreen.screens {
                let point = CGPoint(x: display.visibleFrame.midX, y: display.visibleFrame.midY)
                let quartz = CGPoint(x: point.x, y: primaryTop - point.y)
                recordInputClick(quartz)
                try check(!session.typing && !positioned, "a new click starts a fresh input placement")
                processKey(.keyDown, 32, [], false, chinese: true)
                let anchor = CGRect(x: point.x, y: point.y - 1, width: 1, height: 1)
                let expected = InputPlacement.place(anchor: anchor, size: panel.frame.size, screen: display.visibleFrame)
                try check(panel.isVisible && keyboard.session.pending == "u" &&
                          abs(panel.frame.minX - expected.x) <= 1 && abs(panel.frame.minY - expected.y) <= 1 &&
                          display.visibleFrame.contains(panel.frame),
                          "unknown first key uses the click's screen with or without Accessibility: actual=\(panel.frame), expected=\(expected), screen=\(display.visibleFrame), visible=\(panel.isVisible)")
                let origin = panel.frame.origin
                processKey(.keyDown, 37, [], false, chinese: true)
                requestInputCheck()
                try check(panel.isVisible && keyboard.session.pair == "ul" && panel.frame.origin == origin,
                          "continued keys and background checks keep the estimated position steady")
            }
        }
        let click = lastInputClick!
        settings.followInput = false; applySettings(); reveal()
        let fixedOrigin = panel.frame.origin
        recordInputClick(CGPoint(x: click.point.x + 100, y: click.point.y))
        processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible && panel.frame.origin == fixedOrigin, "click estimates never move fixed mode")

        settings.followInput = true; applySettings()
        provider.result = .allowed(normal)
        reveal(); processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.frame.origin == caretOrigin, "a verified caret takes priority over the last click")
        provider.result = .allowed(InputTarget(element: normal.element, caret: nil,
                                               field: CGRect(x: 1100, y: 100, width: 200, height: 40)))
        resetContext(); processKey(.keyDown, 32, [], false, chinese: true)
        let fieldOrigin = panel.frame.origin
        provider.result = .unknown
        requestInputCheck()
        try check(panel.frame.origin == fieldOrigin, "lost geometry keeps the last known placement during typing")

        provider.deferred = true
        processKey(.keyDown, 37, [], false, chinese: true)
        recordInputClick(click.point)
        provider.finish(.allowed(normal))
        try check(!panel.isVisible && !session.typing && !positioned,
                  "a click invalidates a pending result from the previous input location")
        provider.deferred = false
        processKey(.keyDown, 32, [], false, chinese: true)
        perform("close"); recordInputClick(click.point)
        processKey(.keyDown, 37, [], false, chinese: true)
        try check(!panel.isVisible && session.manuallyHidden, "click positioning never changes the user's closed state")
        foregroundPID = { 5678 }; reveal()
        restorePosition()
        let otherSaved = panel.frame.origin
        resetContext(); processKey(.keyDown, 32, [], false, chinese: true)
        try check(panel.isVisible && panel.frame.origin == otherSaved,
                  "switching apps without a click uses the saved position instead of another app's click")
        foregroundPID = { 1234 }; lastInputClick = nil
        panel.ignoresMouseEvents = false
        resetContext()
        print("PASS: \(checks) privacy and visibility checks across full/compact and fixed/automatic modes")
    }

    /// A separate blank-field app supplies real cross-process AX metadata. The test
    /// driver inherits the terminal's existing permission; the fixture needs none.
    func runNativePrivacyTests() throws {
        let args = CommandLine.arguments
        guard AXIsProcessTrusted(), let index = args.firstIndex(of: "--fixture-app"), index + 1 < args.count else {
            throw NSError(domain: "PrivacyTests", code: 1, userInfo: [NSLocalizedDescriptionKey: "Use test.sh --privacy with existing Accessibility permission"])
        }
        let before = NSWorkspace.shared.frontmostApplication
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("keyshadow-privacy-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        var fixture: NSRunningApplication?
        defer { fixture?.terminate(); try? FileManager.default.removeItem(at: directory); before?.activate(options: []) }
        func wait(_ finished: () -> Bool) throws {
            let until = Date(timeIntervalSinceNow: 10)
            while !finished() && Date() < until { RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.01)) }
            if !finished() { throw NSError(domain: "PrivacyTests", code: 2, userInfo: [NSLocalizedDescriptionKey: "Native privacy test timed out"]) }
        }
        let config = NSWorkspace.OpenConfiguration()
        config.arguments = ["--privacy-fixture", directory.path]; config.createsNewApplicationInstance = true
        var launched = false, launchError: Error?
        NSWorkspace.shared.openApplication(at: URL(fileURLWithPath: args[index + 1]), configuration: config) { app, error in
            fixture = app; launchError = error; launched = true
        }
        try wait { launched }; if let launchError { throw launchError }
        guard let fixture else { throw NSError(domain: "PrivacyTests", code: 3) }
        inspector = InputInspector()
        accessibilityTrusted = { AXIsProcessTrusted() }
        foregroundPID = { fixture.processIdentifier }
        _ = refreshContext()
        var checks = 0, behaviorChecks = 0
        func inspect(_ mode: Int, _ allowed: Bool?, _ label: String) throws {
            checks += 1
            let command = "\(checks):\(mode)"
            try command.write(to: directory.appendingPathComponent("command"), atomically: true, encoding: .utf8)
            try wait { (try? String(contentsOf: directory.appendingPathComponent("ready"), encoding: .utf8)) == command }
            var target: InputTarget?, detected: Bool?
            let metadataDeadline = Date(timeIntervalSinceNow: 2)
            // WebKit's AX focus can lag behind the fixture's DOM focus acknowledgement.
            repeat {
                var finished = false
                var result: InputInspection = .unknown
                InputInspector().inspect(pid: fixture.processIdentifier, includeCaret: true) { result = $0; finished = true }
                try wait { finished }
                switch result {
                case .allowed(let verified): target = verified; detected = true
                case .blocked: target = nil; detected = false
                case .unknown: target = nil; detected = nil
                }
                if detected == allowed { break }
                RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.05))
            } while Date() < metadataDeadline
            if detected != allowed { throw NSError(domain: "PrivacyTests", code: 4, userInfo: [NSLocalizedDescriptionKey: label + " (detected: \(String(describing: detected)))"]) }
            let allowsHints = allowed != false
            for follow in [false, true] {
                for compact in [false, true] {
                    // Cover both display modes: fixed runs always-show, automatic shows while typing.
                    settings.followInput = follow; settings.alwaysShow = !follow; settings.compact = compact
                    applySettings(); resetContext()
                    if allowsHints && follow && target?.caret != nil {
                        panel.setFrameOrigin(screen.visibleFrame.origin)
                    }
                    let beforeInput = panel.frame.origin
                    processKey(.keyDown, 32, [], false, chinese: true)
                    try wait { pendingChecks == 0 }
                    renderVisibility()
                    let expectedVisible = allowsHints || settings.alwaysShow
                    behaviorChecks += 1
                    guard panel.isVisible == expectedVisible, privacyAllowed == allowsHints,
                          allowsHints || (session.pending.isEmpty && flashes.isEmpty) else {
                        throw NSError(domain: "PrivacyTests", code: 5, userInfo: [NSLocalizedDescriptionKey:
                            "\(label) (typing): follow=\(follow) compact=\(compact) visible=\(panel.isVisible) expected=\(expectedVisible) privacy=\(privacyAllowed) allowed=\(String(describing: allowed))"])
                    }
                    if allowsHints && follow && target?.caret != nil {
                        behaviorChecks += 1
                        guard panel.frame.origin != beforeInput else {
                            throw NSError(domain: "PrivacyTests", code: 8, userInfo: [NSLocalizedDescriptionKey: label + " (caret position did not move overlay)"])
                        }
                    }
                    if allowsHints {
                        processKey(.keyDown, 37, [], false, chinese: true)
                        behaviorChecks += 1
                        guard panel.isVisible else {
                            throw NSError(domain: "PrivacyTests", code: 7, userInfo: [NSLocalizedDescriptionKey: label + " (same field flicker)"])
                        }
                        try wait { pendingChecks == 0 }
                    }
                    reveal(); try wait { pendingChecks == 0 }; renderVisibility()
                    behaviorChecks += 1
                    guard panel.isVisible == expectedVisible else {
                        throw NSError(domain: "PrivacyTests", code: 6, userInfo: [NSLocalizedDescriptionKey: label + " (manual preview)"])
                    }
                }
            }
        }
        for _ in 0..<3 {
            try inspect(0, true, "AppKit plain text must be allowed")
            try inspect(1, false, "AppKit secure text must be blocked")
        }
        try inspect(2, false, "AppKit read-only text must be blocked")
        for _ in 0..<3 {
            try inspect(3, true, "WebKit plain text must be allowed")
            try inspect(4, false, "WebKit password must be blocked")
        }
        try inspect(5, false, "WebKit same element changing to password must be blocked")
        try inspect(6, true, "WebKit normal input recovers")
        try inspect(7, true, "WebKit textarea must be allowed")
        try inspect(8, false, "WebKit read-only textarea must be blocked")
        try inspect(9, nil, "unrecognized focused window defaults to highlights")
        settings.alwaysShow = false; applySettings()
        resetContext()
        print("PASS: \(checks) cross-process AppKit/WebKit metadata checks and \(behaviorChecks) real-provider visibility checks using blank fields")
    }
}
