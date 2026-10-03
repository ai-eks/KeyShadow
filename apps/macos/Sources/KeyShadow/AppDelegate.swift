import AppKit
import Carbon
import KeyShadowCore
import ServiceManagement

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    var settings = Settings.load()
    var session = InputSession()
    let panel = OverlayPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
    let keyboard = KeyboardView(frame: .zero)
    let monitor = KeyboardMonitor()
    var inspector: InputInspecting = InputInspector()
    var inputSource = InputSourceState.current
    var foregroundPID = { NSWorkspace.shared.frontmostApplication?.processIdentifier ?? 0 }
    var accessibilityTrusted = { AXIsProcessTrusted() }
    var secureInputEnabled = { IsSecureEventInputEnabled() }
    var uptime = { ProcessInfo.processInfo.systemUptime }
    var privacyAllowed = false
    var inputFocus: AXUIElement?
    var checkID = 0
    var lastAppliedCheck = 0
    var pendingChecks = 0
    var hotkeys: GlobalHotKeys?
    var clickMonitor: Any?
    var lastInputClick: (pid: pid_t, point: CGPoint)?
    var statusItem: NSStatusItem?
    var permissions: PermissionsWindow?
    var timer: Timer?
    var menuOpen = false
    var pendingMenuReveal = false
    var collapsed = false
    var dragging = false
    var dragStart: CGPoint?
    var positioned = false
    var generation = 0
    var context = InputSourceState.current()
    var frontPID: pid_t = 0
    var lastContextCheck: TimeInterval = 0
    var flashes: [String: TimeInterval] = [:]
    var pressedKeys: Set<String> = []
    let smoke = CommandLine.arguments.contains("--smoke-test")
    var now: TimeInterval { uptime() }
    /// "Always show" keeps a static keyboard on screen; placement is a separate setting.
    var staticVisible: Bool { settings.alwaysShow && !session.manuallyHidden && !collapsed }
    /// build.sh stamps the bundle from the repository's VERSION file.
    var appVersion: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "开发版" }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if smoke { settings = Settings() }
        configurePanel()
        applySettings()
        if smoke {
            Timer.scheduledTimer(withTimeInterval: 0.1, repeats: false) { _ in
                do { try self.runSmokeTests(); exit(0) }
                catch { fputs("FAIL: \(error)\n", stderr); exit(1) }
            }
            return
        }
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let url = Bundle.main.url(forResource: "logo", withExtension: "png"), let image = NSImage(contentsOf: url) {
            let side = NSStatusBar.system.thickness - 4
            image.size = NSSize(width: side, height: side); statusItem?.button?.image = image
        } else { statusItem?.button?.title = "键影" }
        statusItem?.button?.toolTip = "键影 · 双拼输入提示"
        statusItem?.button?.setAccessibilityLabel("键影")
        statusItem?.menu = makeMenu()
        monitor.onKey = { [weak self] type, key, flags, repeated in self?.handleKey(type, key, flags, repeated) }
        monitor.start()
        clickMonitor = NSEvent.addGlobalMonitorForEvents(matching: .leftMouseUp) { [weak self] event in
            if let point = event.cgEvent?.location { self?.recordInputClick(point) }
        }
        hotkeys = GlobalHotKeys()
        hotkeys?.action = { [weak self] id in self?.perform(id == 1 ? "toggle" : "pass") }
        timer = Timer(timeInterval: 0.04, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(timer!, forMode: .common)
        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged),
                                               name: NSApplication.didChangeScreenParametersNotification, object: nil)
        if !UserDefaults.standard.bool(forKey: "welcomed") {
            UserDefaults.standard.set(true, forKey: "welcomed")
            reveal()
            if !CGPreflightListenEventAccess() { showPermissions() }
        }
    }

    func configurePanel() {
        panel.title = "键影 · 双拼输入提示"
        panel.isOpaque = false; panel.backgroundColor = .clear
        panel.hasShadow = true; panel.level = .floating
        panel.hidesOnDeactivate = false; panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        panel.contentView = keyboard
        keyboard.onAction = { [weak self] name, sender in self?.perform(name, sender: sender) }
        keyboard.onContextMenu = { [weak self] event in
            guard let self else { return }
            NSMenu.popUpContextMenu(self.makeMenu(), with: event, for: self.keyboard)
        }
        keyboard.onDrag = { [weak self] origin, ended in
            guard let self else { return }
            if !ended {
                self.dragStart = origin; self.dragging = true
                return
            }
            let started = self.dragStart
            self.dragStart = nil; self.dragging = false
            guard let started, origin != started else { return }
            self.settings.followInput = false
            self.panel.setFrameOrigin(self.dragOrigin(origin, pointer: NSEvent.mouseLocation))
            self.settings.x = self.panel.frame.minX; self.settings.y = self.panel.frame.minY
            self.applySettings(); self.reveal()
        }
    }

    func dragOrigin(_ origin: CGPoint, pointer: CGPoint) -> CGPoint {
        let destination = NSScreen.screens.first { $0.frame.contains(pointer) } ?? screen
        return InputPlacement.clamp(origin, size: panel.frame.size, screen: destination.visibleFrame)
    }

    var screen: NSScreen {
        NSScreen.screens.first { $0.frame.contains(panel.frame.origin) } ?? NSScreen.main ?? NSScreen.screens[0]
    }

    func applySettings() {
        settings.normalize()
        session.delay = settings.delay
        keyboard.settings = settings; keyboard.session = session
        if keyboard.guide.scheme.id != settings.scheme { keyboard.guide = PinyinGuide(scheme: PinyinData.bundled.scheme(settings.scheme)) }
        let size = keyboard.logicalSize
        let visible = screen.visibleFrame
        keyboard.scale = min(settings.scale, (visible.width - 24) / size.width, (visible.height - 24) / size.height)
        let top = panel.frame.maxY
        panel.setContentSize(NSSize(width: size.width * keyboard.scale, height: size.height * keyboard.scale))
        if top > 0 { panel.setFrameOrigin(CGPoint(x: panel.frame.minX, y: top - panel.frame.height)) }
        else { restorePosition() }
        panel.alphaValue = settings.opacity
        keyboard.passThrough = panel.ignoresMouseEvents
        keyboard.rebuildControls()
        keepOnScreen()
        if !smoke { settings.save() }
    }

    func restorePosition() {
        let work = screen.visibleFrame
        panel.setFrameOrigin(CGPoint(x: settings.x ?? work.midX - panel.frame.width / 2,
                                     y: settings.y ?? work.minY + 40))
        keepOnScreen()
    }

    func keepOnScreen() {
        panel.setFrameOrigin(InputPlacement.clamp(panel.frame.origin, size: panel.frame.size, screen: screen.visibleFrame))
    }

    @objc func screensChanged() { generation += 1; applySettings() }

    func reveal() {
        pendingMenuReveal = false
        collapsed = false
        _ = refreshContext()
        resetContext()
        panel.ignoresMouseEvents = false
        session.preview(at: now)
        applySettings(); requestInputCheck()
    }

    func resetContext(invalidateChecks: Bool = true) {
        session.reset(); flashes.removeAll(); pressedKeys.removeAll(); positioned = false
        if invalidateChecks { generation += 1 }
        privacyAllowed = false; inputFocus = nil
        keyboard.session = session; keyboard.flashes = flashes
        if !staticVisible { panel.orderOut(nil) }
    }

    @discardableResult func refreshContext() -> Bool {
        let source = inputSource()
        let pid = foregroundPID()
        let changed = source != context || pid != frontPID
        if changed { context = source; frontPID = pid; resetContext() }
        return context.chinese && !CGEventSource.flagsState(.combinedSessionState).contains(.maskAlphaShift)
            && !secureInputEnabled() && pid != ProcessInfo.processInfo.processIdentifier
    }

    func handleKey(_ type: CGEventType, _ code: UInt16, _ flags: CGEventFlags, _ repeated: Bool) {
        if type == .keyUp { releaseKey(code); return }
        guard !menuOpen, !dragging else { return }
        let chinese = refreshContext()
        processKey(type, code, flags, repeated, chinese: chinese)
    }

    func recordInputClick(_ point: CGPoint) {
        let pid = foregroundPID()
        guard !menuOpen, !dragging, pid > 0, pid != ProcessInfo.processInfo.processIdentifier else { return }
        // Global mouse events exclude our own controls. Keep Quartz screen coordinates,
        // and scope the estimate to this app so switching apps cannot reuse its click.
        lastInputClick = (pid, point)
        if settings.followInput { resetContext() }
    }

    // The event tap and desktop checks share this dispatch path. OS input-source
    // detection remains outside it so tests never alter the user's input method.
    func processKey(_ type: CGEventType, _ code: UInt16, _ flags: CGEventFlags, _ repeated: Bool, chinese: Bool) {
        guard !menuOpen, !dragging else { return }
        let modifiers: CGEventFlags = [.maskShift, .maskControl, .maskAlternate, .maskCommand]
        // Carbon owns these shortcuts; the passive tap must not hide its preview afterwards.
        if (code == kVK_F8 || code == kVK_F9) && flags.intersection(modifiers) == [.maskControl, .maskAlternate] { return }
        if type == .flagsChanged {
            if code == kVK_CapsLock && flags.contains(.maskAlphaShift) { resetContext() }
            return
        }
        if type == .keyUp { releaseKey(code); return }
        guard type == .keyDown else { return }
        guard chinese, flags.intersection(modifiers).isEmpty else { resetContext(); return }
        guard !session.manuallyHidden, !secureInputEnabled() else { resetContext(); return }
        let letter = KeyboardMonitor.letters[code]
        if let letter, letter != ";" || keyboard.guide.scheme.usesSemicolon {
            collapsed = false
            pressedKeys.insert(letter)
            requestInputCheck(key: letter, repeated: repeated)
        } else if code == kVK_Delete {
            requestInputCheck(key: "backspace", repeated: repeated)
        } else {
            // Any other key (space, digits, punctuation, Return, Esc, arrows…) ends the input.
            resetContext()
        }
    }

    /// A held key stays highlighted until it is released, like the Windows keyboard.
    func releaseKey(_ code: UInt16) {
        guard let letter = KeyboardMonitor.letters[code], pressedKeys.remove(letter) != nil else { return }
        keyboard.needsDisplay = true
    }

    func requestInputCheck(key: String? = nil, repeated: Bool = false) {
        guard !session.manuallyHidden, !secureInputEnabled() else { resetContext(); return }
        checkID += 1
        let request = checkID, version = generation, pid = frontPID, started = now
        let hover = panel.isVisible && !panel.ignoresMouseEvents && panel.frame.contains(NSEvent.mouseLocation)
        // Without Accessibility every app uses the same unknown-state fallback.
        let skipInspection = !accessibilityTrusted()
        pendingChecks += 1
        var keepVisible = privacyAllowed && (key == nil || skipInspection)
        if key != nil, privacyAllowed, panel.isVisible, !skipInspection {
            switch InputInspector.read(pid: pid, includeCaret: false, messagingTimeout: 0.03) {
            case .allowed(let current): keepVisible = inputFocus.map { CFEqual($0, current.element) } ?? false
            case .blocked: keepVisible = false
            case .unknown: keepVisible = true
            }
        }
        // A known changed or blocked field hides; unavailable metadata keeps the fallback visible.
        if !keepVisible {
            privacyAllowed = false
            keyboard.session = InputSession(); keyboard.flashes = [:]; keyboard.needsDisplay = true
            if !staticVisible { panel.orderOut(nil) }
        }
        var finished = false
        let complete: (InputInspection) -> Void = { [weak self] result in
            guard let self, !finished else { return }
            finished = true
            self.pendingChecks -= 1
            guard version == self.generation, request > self.lastAppliedCheck else { return }
            guard pid == self.foregroundPID(), self.inputSource() == self.context,
                  !self.secureInputEnabled() else { self.resetContext(); return }
            self.lastAppliedCheck = request
            let result: InputInspection = self.now - started < InputInspector.timeout ? result : .unknown
            let target: InputTarget?
            switch result {
            case .allowed(let verified): target = verified
            case .unknown: target = nil
            case .blocked:
                // Reject this key without discarding a newer key's independent check.
                self.resetContext(invalidateChecks: request == self.checkID)
                return
            }
            if let previous = self.inputFocus, let target, !CFEqual(previous, target.element) {
                self.session.reset(); self.flashes.removeAll(); self.positioned = false
            }
            self.inputFocus = target?.element
            if let key {
                if repeated && key != "backspace" {
                    if self.session.typing { self.flashes[key] = self.now + 0.16; self.session.refreshDeadline(at: self.now) }
                } else if key == ";" && self.session.pending.isEmpty {
                    self.session.reset(); self.flashes.removeAll()
                } else {
                    self.session.press(key, at: self.now)
                    if key != "backspace" { self.flashes[key] = self.now + 0.16 }
                }
            }
            // Earlier keys may complete while the next key's privacy check is pending.
            // Preserve the pair in order, but only the newest verdict may show it.
            guard request == self.checkID, !self.menuOpen, !self.dragging else { return }
            self.privacyAllowed = true
            if self.settings.followInput, !hover {
                if let rect = target?.caret ?? target?.field { self.placeInput(rect) }
                else if !self.positioned {
                    if let click = self.lastInputClick, click.pid == pid {
                        self.placeInput(CGRect(origin: click.point, size: CGSize(width: 1, height: 1)))
                    } else { self.restorePosition() }
                }
            }
            self.positioned = true
            self.renderVisibility()
        }
        if skipInspection { complete(.unknown) }
        else {
            inspector.inspect(pid: pid, includeCaret: settings.followInput, completion: complete)
            DispatchQueue.main.asyncAfter(deadline: .now() + InputInspector.timeout) { complete(.unknown) }
        }
    }

    func placeInput(_ rect: CGRect) {
        guard let primary = NSScreen.screens.first else { return }
        // AX uses a top-left origin on the primary display, including for other monitors.
        let anchor = CGRect(x: rect.minX, y: primary.frame.maxY - rect.maxY, width: rect.width, height: rect.height)
        let display = NSScreen.screens.first { $0.frame.intersects(anchor) } ?? screen
        let size = keyboard.logicalSize
        let fit = min(settings.scale, (display.visibleFrame.width - 24) / size.width,
                      (display.visibleFrame.height - 24) / size.height)
        if fit != keyboard.scale {
            keyboard.scale = fit
            panel.setContentSize(CGSize(width: size.width * fit, height: size.height * fit))
            keyboard.rebuildControls()
        }
        panel.setFrameOrigin(InputPlacement.place(anchor: anchor, size: panel.frame.size, screen: display.visibleFrame))
    }

    func tick() {
        if dragging && NSEvent.pressedMouseButtons & 1 == 0 {
            keyboard.onDrag?(panel.frame.origin, true)
        }
        let pid = foregroundPID()
        if pendingMenuReveal && !menuOpen && pid > 0 && pid != ProcessInfo.processInfo.processIdentifier {
            pendingMenuReveal = false
            reveal()
        }
        if secureInputEnabled() { resetContext() }
        if now - lastContextCheck >= 0.2 {
            lastContextCheck = now
            if !menuOpen && !dragging {
                let wasPreview = session.previewUntil > now
                let chinese = refreshContext()
                if !wasPreview && !chinese { resetContext() }
                if pendingChecks == 0 && (session.typing || session.previewUntil > 0 || !session.pending.isEmpty) {
                    requestInputCheck()
                }
                if !CGPreflightListenEventAccess() { monitor.stop(); if !wasPreview { resetContext() } }
                else { monitor.start() }
            }
            permissions?.refresh()
        }
        pruneFlashes()
        renderVisibility()
    }

    /// A tap stays lit for 160 ms; a held key stays lit until it is released.
    func pruneFlashes() {
        let oldCount = flashes.count
        flashes = flashes.filter { $0.value > now || pressedKeys.contains($0.key) }
        if oldCount != flashes.count { keyboard.needsDisplay = true }
    }

    func renderVisibility() {
        let safe = privacyAllowed && !secureInputEnabled()
        let visible = session.visible(at: now, paused: menuOpen || dragging)
        let showHints = safe && session.typing
        keyboard.session = showHints ? session : InputSession()
        keyboard.flashes = showHints ? flashes : [:]
        keyboard.monitorRunning = monitor.running || smoke
        if staticVisible {
            if !panel.isVisible { panel.orderFrontRegardless() }
        } else if safe && visible && (session.previewUntil > 0 || positioned) {
            if !panel.isVisible { panel.orderFrontRegardless() }
        } else if !safe || (!menuOpen && !dragging) { panel.orderOut(nil) }
        keyboard.needsDisplay = true
    }

    func perform(_ command: String, sender: NSView? = nil) {
        let parts = command.split(separator: "=", maxSplits: 1).map(String.init)
        let value = parts.count > 1 ? parts[1] : ""
        switch parts[0] {
        case "toggle": if session.manuallyHidden { reveal() } else { perform("close") }; return
        case "reveal": reveal(); return
        case "hide": pendingMenuReveal = false; collapsed = true; resetContext(); return
        case "close":
            pendingMenuReveal = false; resetContext(); session.close(); panel.orderOut(nil)
            statusItem?.menu?.cancelTracking(); return
        case "quit": NSApp.terminate(nil); return
        case "permissions": showPermissions(); return
        case "startup":
            do {
                if SMAppService.mainApp.status == .enabled || SMAppService.mainApp.status == .requiresApproval {
                    try SMAppService.mainApp.unregister()
                } else { try SMAppService.mainApp.register() }
                if SMAppService.mainApp.status == .requiresApproval { SMAppService.openSystemSettingsLoginItems() }
            } catch { showError("无法更改登录启动", error.localizedDescription) }
            return
        case "loginSettings": SMAppService.openSystemSettingsLoginItems(); return
        case "scheme", "theme", "delay", "size", "opacity":
            if value.isEmpty, let sender {
                let menu = choiceMenu(parts[0]); menu.delegate = self
                menu.popUp(positioning: nil, at: NSPoint(x: 0, y: -menu.size.height - 4), in: sender)
                return
            }
            switch parts[0] {
            case "scheme":
                settings.scheme = value; session.reset(); generation += 1
                if panel.isVisible { session.preview(at: now) }
            case "theme": settings.theme = value
            case "delay": settings.delay = Double(value) ?? 2; session.refreshDeadline(at: now)
            case "size": settings.scale = Double(value) ?? 1
            default: settings.opacity = Double(value) ?? 0.85
            }
        case "compact": keyboard.showRules = false; settings.compact.toggle()
        case "rules": keyboard.showRules.toggle()
        case "follow":
            settings.followInput.toggle(); collapsed = false; resetContext()
            if !settings.followInput { restorePosition() }
        case "always": settings.alwaysShow.toggle(); collapsed = false; resetContext()
        case "hints":
            settings.hints.toggle(); session.reset(); generation += 1
            if panel.isVisible { session.preview(at: now) }
        case "pass": panel.ignoresMouseEvents.toggle()
        // Step from the displayed size; the saved size can exceed what a small screen fits.
        case "smaller": settings.scale = (Double(keyboard.scale) * 100).rounded() / 100 - 0.05
        case "larger": settings.scale = (Double(keyboard.scale) * 100).rounded() / 100 + 0.05
        case "fainter": settings.opacity -= 0.05
        case "opaque": settings.opacity += 0.05
        case "reset":
            // Position modes stay unchanged; the keyboard reappears at the default spot.
            settings.x = nil; settings.y = nil; settings.scale = 1; settings.opacity = 0.85
            applySettings(); restorePosition(); reveal(); return
        default: return
        }
        applySettings()
    }

    func item(_ title: String, _ command: String, checked: Bool = false) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: #selector(menuAction(_:)), keyEquivalent: "")
        item.target = self; item.representedObject = command; item.state = checked ? .on : .off
        return item
    }
    @objc func menuAction(_ sender: NSMenuItem) {
        let command = sender.representedObject as! String
        if command == "reveal" && menuOpen { session.resume(); collapsed = false; pendingMenuReveal = true }
        else { perform(command) }
    }

    func choiceMenu(_ type: String) -> NSMenu {
        let menu = NSMenu(); menu.autoenablesItems = false
        var choices: [(String, String)] = []; var selected = ""
        switch type {
        case "scheme": choices = PinyinData.bundled.schemes.map { ($0.name, $0.id) }; selected = settings.scheme
        case "theme": choices = Theme.all.map { ($0.name, $0.id) }; selected = settings.theme
        case "delay":
            choices = [1, 2, 3, 5, 10, 30, 0].map { (seconds: Int) -> (String, String) in
                let title = seconds == 0 ? "不因停顿收起" : seconds == 2 ? "2 秒（默认）" : "\(seconds) 秒"
                return (title, String(seconds))
            }
            selected = String(Int(settings.delay))
        case "size": choices = [50, 60, 75, 90, 100, 115, 130, 150].map { ("\($0)%", String(Double($0) / 100)) }; selected = String(settings.scale)
        default: choices = [35, 50, 65, 75, 85, 95].map { ("\($0)%", String(Double($0) / 100)) }; selected = String(settings.opacity)
        }
        choices.forEach { menu.addItem(item($0.0, type + "=" + $0.1, checked: selected == $0.1)) }
        return menu
    }

    func makeMenu() -> NSMenu {
        let menu = NSMenu(); menu.autoenablesItems = false; menu.delegate = self
        populate(menu); return menu
    }

    func populate(_ menu: NSMenu) {
        menu.removeAllItems()
        menu.addItem(item((session.manuallyHidden ? "恢复提示" : "关闭提示") + "    ⌃⌥F8",
                          session.manuallyHidden ? "reveal" : "close"))
        menu.addItem(item("收起键盘（下次输入自动显示）", "hide"))
        menu.addItem(item("鼠标穿透    ⌃⌥F9", "pass", checked: panel.ignoresMouseEvents))
        menu.addItem(.separator())
        for (title, type) in [("双拼方案", "scheme"), ("配色主题", "theme"), ("停止输入后收起", "delay"), ("键盘大小", "size"), ("不透明度", "opacity")] {
            let parent = NSMenuItem(title: title, action: nil, keyEquivalent: "")
            parent.submenu = choiceMenu(type); menu.addItem(parent)
        }
        menu.addItem(item("紧凑模式", "compact", checked: settings.compact))
        menu.addItem(item("输入时跟随位置", "follow", checked: settings.followInput))
        menu.addItem(item("一直显示键盘", "always", checked: settings.alwaysShow))
        menu.addItem(item("下一键韵母提示", "hints", checked: settings.hints))
        let rules = item("零声母规则", "rules", checked: keyboard.showRules)
        rules.isEnabled = !settings.compact; menu.addItem(rules)
        menu.addItem(item("恢复位置与透明度", "reset"))
        menu.addItem(.separator())
        let status = SMAppService.mainApp.status
        menu.addItem(item(status == .requiresApproval ? "登录时启动（等待系统批准）" : "登录时启动", "startup", checked: status == .enabled))
        if status == .requiresApproval { menu.addItem(item("打开登录项设置", "loginSettings")) }
        menu.addItem(item("权限设置…", "permissions"))
        if hotkeys?.available == false {
            let unavailable = NSMenuItem(title: "部分快捷键被占用 · 请使用菜单栏", action: nil, keyEquivalent: "")
            unavailable.isEnabled = false; menu.addItem(unavailable)
        }
        menu.addItem(.separator())
        let version = NSMenuItem(title: "版本 \(appVersion)", action: nil, keyEquivalent: "")
        version.isEnabled = false; menu.addItem(version)
        menu.addItem(item("退出键影", "quit"))
    }

    func menuNeedsUpdate(_ menu: NSMenu) { if menu === statusItem?.menu { populate(menu) } }
    func menuWillOpen(_ menu: NSMenu) { menuOpen = true; generation += 1; flashes.removeAll() }
    func menuDidClose(_ menu: NSMenu) {
        menuOpen = false; session.refreshDeadline(at: now)
        if pendingMenuReveal { return }
        if session.previewUntil > 0 { session.preview(at: now) }
        if !session.manuallyHidden && (session.typing || session.previewUntil > 0) { requestInputCheck() }
    }

    func showPermissions() {
        if permissions == nil { permissions = PermissionsWindow() }
        permissions?.show()
    }

    func showError(_ title: String, _ message: String) {
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = message
        NSApp.activate(ignoringOtherApps: true); alert.runModal()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { reveal(); return true }
    func applicationWillTerminate(_ notification: Notification) {
        monitor.stop(); timer?.invalidate()
        if let clickMonitor { NSEvent.removeMonitor(clickMonitor) }
    }
}
