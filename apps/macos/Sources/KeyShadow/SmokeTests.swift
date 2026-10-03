import AppKit
import KeyShadowCore

extension AppDelegate {
    /// Opt-in desktop/render checks. Does not monitor keys, change TCC, login items, or saved settings.
    func runSmokeTests() throws {
        let provider = TestInputInspector()
        inspector = provider
        inputSource = { InputSourceState(id: "test.chinese", chinese: true) }
        foregroundPID = { 1234 }
        secureInputEnabled = { false }
        accessibilityTrusted = { true }
        let args = CommandLine.arguments
        let output = args.firstIndex(of: "--output").flatMap { $0 + 1 < args.count ? args[$0 + 1] : nil }
            ?? FileManager.default.temporaryDirectory.appendingPathComponent("keyshadow-smoke").path
        try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
        func check(_ condition: @autoclosure () -> Bool, _ label: String) throws {
            if !condition() { throw NSError(domain: "SmokeTests", code: 1, userInfo: [NSLocalizedDescriptionKey: label]) }
        }
        func capture(_ name: String) throws {
            keyboard.displayIfNeeded()
            guard let bitmap = keyboard.bitmapImageRepForCachingDisplay(in: keyboard.bounds) else {
                throw NSError(domain: "SmokeTests", code: 2)
            }
            keyboard.cacheDisplay(in: keyboard.bounds, to: bitmap)
            try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: output).appendingPathComponent(name + ".png"))
        }
        let before = NSWorkspace.shared.frontmostApplication?.processIdentifier
        reveal(); renderVisibility()
        try check(panel.isVisible, "preview shows the real panel")
        try check(!panel.canBecomeKey && !panel.canBecomeMain && !panel.isKeyWindow, "overlay never takes keyboard focus")
        try check(NSWorkspace.shared.frontmostApplication?.processIdentifier == before, "preview preserves frontmost application")
        try check(keyboard.subviews.count == 16, "full view has all interactive controls")
        let bottom = ["scheme", "theme", "delay", "follow", "always", "hints", "rules"].compactMap { id in
            keyboard.subviews.first { $0.identifier?.rawValue == id }?.frame
        }
        try check(bottom.count == 7 && zip(bottom, bottom.dropFirst()).allSatisfy { $0.maxX < $1.minX && $0.minY == $1.minY },
                  "bottom bar holds seven non-overlapping controls in the shared order")
        try check(keyboard.subviews.allSatisfy { ($0 as? NSButton)?.toolTip?.isEmpty == false },
                  "every control explains itself with a tooltip")
        for (id, title) in [("pass", "穿透"), ("close", "关闭"), ("quit", "退出")] {
            try check(keyboard.subviews.contains { $0.identifier?.rawValue == id && ($0 as? NSButton)?.title == title }, "top action label: " + title)
        }
        try capture("macos-preview")
        session.press("u", at: now); renderVisibility()
        try capture("macos-next-key")
        session.press("l", at: now); renderVisibility()
        try check(keyboard.guide.decode(session.pair) == "shuang", "production guide decodes UL")
        try capture("macos-result")
        perform("scheme=microsoft"); reveal()
        session.press("l", at: now); session.press(";", at: now); renderVisibility()
        try check(keyboard.guide.decode(session.pair) == "ling", "Microsoft semicolon integration")
        try capture("macos-microsoft")
        perform("compact")
        try check(keyboard.subviews.count == 2 && keyboard.logicalSize.height == 210, "compact keeps hide and expand")
        try check(keyboard.subviews.allSatisfy { keyboard.bounds.contains($0.frame) }, "compact actions fit within panel")
        try capture("macos-compact")
        perform("hide")
        try check(!panel.isVisible && !session.manuallyHidden && session.pending.isEmpty, "collapse waits for new input")
        reveal()
        try check(panel.isVisible && settings.compact && !session.manuallyHidden, "reveal preserves compact mode")
        perform("pass"); try check(panel.ignoresMouseEvents, "click-through enabled")
        reveal(); try check(!panel.ignoresMouseEvents, "menu recovery disables click-through")
        processKey(.keyDown, 100, [.maskControl, .maskAlternate], false, chinese: false)
        try check(panel.isVisible && session.previewUntil > now, "event tap does not cancel the F8 preview")
        processKey(.flagsChanged, 59, [.maskAlphaShift], false, chinese: false)
        try check(panel.isVisible, "releasing hotkey modifiers with Caps Lock keeps manual preview")
        perform("compact"); perform("rules")
        try check(keyboard.logicalSize.height == 442, "rules expand full panel")
        try capture("macos-rules")
        perform("rules")
        for theme in Theme.all {
            perform("theme=" + theme.id); try capture("macos-" + theme.id)
        }
        for size in [0.5, 0.85, 1.5] {
            perform("size=\(size)")
            try check(keyboard.subviews.allSatisfy { keyboard.bounds.contains($0.frame) }, "scaled controls fit")
            try check(screen.visibleFrame.contains(panel.frame), "scaled panel stays on-screen")
        }
        let menu = makeMenu()
        try check(menu.items.contains { $0.title == "双拼方案" && $0.submenu?.items.count == 4 }, "four scheme menu entries")
        menuWillOpen(menu)
        try check(session.visible(at: now + 100, paused: menuOpen), "open menu suspends preview expiry")
        menuDidClose(menu)
        try check(session.previewUntil > now, "closing menu renews preview")
        perform("follow")
        try check(!settings.followInput && !session.typing && !panel.isVisible, "switching position waits for input")
        processKey(.keyDown, 37, [], false, chinese: true)
        try check(panel.isVisible && session.pending == "l", "Chinese typing shows fixed overlay")
        try check(pressedKeys.contains("l") && flashes["l"] != nil, "a pressed key is highlighted")
        let realClock = uptime, held = now + 1
        uptime = { held }; pruneFlashes()
        try check(flashes["l"] != nil, "a held key stays highlighted past the tap flash")
        processKey(.keyUp, 37, [], false, chinese: true); pruneFlashes()
        try check(flashes["l"] == nil && !pressedKeys.contains("l"), "releasing the key ends its highlight")
        uptime = realClock
        processKey(.keyDown, 37, [], true, chinese: true)
        try check(session.pending == "l", "OS repeat does not advance the pair")
        processKey(.keyDown, 41, [], false, chinese: true)
        try check(keyboard.guide.decode(session.pair) == "ling", "real key dispatch accepts semicolon")
        processKey(.keyDown, 41, [], true, chinese: true)
        try check(keyboard.guide.decode(session.pair) == "ling", "repeated semicolon preserves completed syllable")
        processKey(.keyDown, 51, [], false, chinese: true)
        try check(session.pending == "l", "real backspace corrects second key")
        menuWillOpen(menu)
        processKey(.keyDown, 0, [], false, chinese: true)
        try check(session.pending == "l", "menu typing never changes pinyin")
        menuDidClose(menu)
        processKey(.keyDown, 0, [.maskCommand], false, chinese: true)
        try check(!panel.isVisible && session.pending.isEmpty, "command shortcuts end input and hide a typing-only keyboard")
        perform("always"); renderVisibility()
        try check(settings.alwaysShow && !settings.followInput && panel.isVisible && !session.typing,
                  "always-show displays a static keyboard without typing")
        processKey(.keyDown, 0, [], false, chinese: false)
        try check(panel.isVisible && session.pending.isEmpty, "English typing leaves only the static keyboard")
        processKey(.keyDown, 51, [], false, chinese: true)
        try check(panel.isVisible && session.pending.isEmpty, "backspace alone never starts hints")
        processKey(.keyDown, 41, [], false, chinese: true)
        try check(panel.isVisible && session.pending.isEmpty, "semicolon alone never starts a syllable")
        processKey(.keyDown, 0, [], false, chinese: true)
        processKey(.flagsChanged, 57, [.maskAlphaShift], false, chinese: true)
        try check(panel.isVisible && session.pending.isEmpty, "Caps Lock clears hints in always-show mode")
        perform("close")
        processKey(.keyDown, 0, [], false, chinese: true)
        try check(!panel.isVisible, "close survives real key dispatch")
        let items = makeMenu().items
        let titles = items.map(\.title)
        try check(titles.contains("一直显示键盘") && titles.contains("零声母规则") && titles.contains("收起键盘（下次输入自动显示）"),
                  "menu offers display mode, rules and temporary collapse")
        try check(items.first { $0.title == "键盘大小" }?.submenu?.items.map(\.title) ==
                  ["50%", "60%", "75%", "90%", "100%", "115%", "130%", "150%"], "size presets match Windows")
        try check(items.first { $0.title == "停止输入后收起" }?.submenu?.items.contains { $0.title == "2 秒（默认）" } == true,
                  "delay menu marks the default")
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        try check(version != nil && items.contains { $0.title == "版本 " + version! && !$0.isEnabled } &&
                  titles.firstIndex(of: "版本 " + version!) == titles.firstIndex(of: "退出键影").map { $0 - 1 },
                  "menu shows the bundle version just above quit")
        perform("always"); reveal()
        settings.scale = 1.5; applySettings()
        let shown = keyboard.scale
        perform("smaller")
        try check(keyboard.scale < shown - 0.04, "minus steps from the displayed size even when the screen limits it")
        perform("larger")
        try check(abs(keyboard.scale - shown) < 0.011, "plus returns to the displayed size")
        perform("pass"); perform("opacity=0.5"); perform("size=0.75")
        perform("reset")
        try check(!panel.ignoresMouseEvents && settings.scale == 1 && settings.opacity == 0.85 && settings.x == nil &&
                  !settings.followInput && panel.isVisible && session.previewUntil > now,
                  "reset restores size, opacity and position, disables click-through and keeps the position mode")
        try runPrivacyTests(provider)
        if args.contains("--privacy-test") { try runNativePrivacyTests() }
        if NSScreen.screens.count > 1 {
            let source = NSScreen.screens[0], destination = NSScreen.screens[1]
            let origin = CGPoint(x: source.visibleFrame.midX - panel.frame.width / 2,
                                 y: source.visibleFrame.midY - panel.frame.height / 2)
            let landed = dragOrigin(origin, pointer: CGPoint(x: destination.frame.midX, y: destination.frame.midY))
            try check(destination.visibleFrame.contains(CGRect(origin: landed, size: panel.frame.size)),
                      "drag can land on another screen while window origin is still on the first")
        }
        settings.followInput = true
        let dragStart = panel.frame.origin
        keyboard.onDrag?(dragStart, false)
        try check(dragging && settings.followInput, "drag start pauses automatic placement without changing mode")
        keyboard.onDrag?(dragStart, true)
        try check(!dragging && settings.followInput, "click without movement keeps automatic mode")
        keyboard.onDrag?(dragStart, false)
        panel.setFrameOrigin(CGPoint(x: dragStart.x + 20, y: dragStart.y))
        keyboard.onDrag?(panel.frame.origin, true)
        try check(!dragging && !settings.followInput && settings.x == panel.frame.minX && settings.y == panel.frame.minY,
                  "completed native drag saves its fixed position")
        panel.orderOut(nil)
        print("PASS: native panel, focus preservation, controls, compact recovery, semicolon, menus, scale and four themes")
        print("Screenshots: \(output)")
    }
}
