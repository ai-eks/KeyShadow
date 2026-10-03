import AppKit
import KeyShadowCore

final class OverlayPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class OverlayButton: NSButton {
    var fill = NSColor.clear
    var ink = NSColor.labelColor
    var border: NSColor?
    var hoverFill = NSColor.clear
    var hoverInk: NSColor?
    var hoverBorder: NSColor?
    var radius: CGFloat = 7
    private var hovered = false
    private var hoverArea: NSTrackingArea?
    override var acceptsFirstResponder: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let hoverArea { removeTrackingArea(hoverArea) }
        let area = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area); hoverArea = area
    }
    override func mouseEntered(with event: NSEvent) { hovered = true; needsDisplay = true }
    override func mouseExited(with event: NSEvent) { hovered = false; needsDisplay = true }
    override func draw(_ dirtyRect: NSRect) {
        let ink = hovered ? hoverInk ?? self.ink : self.ink
        let base = hovered ? hoverFill : fill
        (isHighlighted ? base.blended(withFraction: 0.15, of: ink)! : base).setFill()
        let path = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: radius, yRadius: radius)
        path.fill()
        if let border = hovered ? hoverBorder ?? border : border { border.setStroke(); path.stroke() }
        let style = NSMutableParagraphStyle()
        style.alignment = .center
        let attrs: [NSAttributedString.Key: Any] = [.font: font!, .foregroundColor: ink, .paragraphStyle: style]
        let height = (title as NSString).size(withAttributes: attrs).height
        (title as NSString).draw(in: NSRect(x: 0, y: (bounds.height - height) / 2,
                                         width: bounds.width, height: height + 2), withAttributes: attrs)
    }
}

final class KeyboardView: NSView {
    var settings = Settings()
    var session = InputSession()
    var guide = PinyinGuide(scheme: PinyinData.bundled.schemes[0])
    var showRules = false
    var passThrough = false
    var monitorRunning = false
    var flashes: [String: TimeInterval] = [:]
    var scale: CGFloat = 1
    var onAction: ((String, NSView?) -> Void)?
    var onDrag: ((CGPoint, Bool) -> Void)?
    var onContextMenu: ((NSEvent) -> Void)?
    private var buttons: [String: OverlayButton] = [:]
    private var theme: Theme { Theme.named(settings.theme) }
    private let logo = Bundle.main.url(forResource: "logo", withExtension: "png").flatMap(NSImage.init(contentsOf:))

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    var logicalSize: CGSize { CGSize(width: settings.compact ? 796 : 820,
                                   height: settings.compact ? 210 : showRules ? 442 : 344) }

    func color(_ name: String) -> NSColor {
        let rgb = UInt32(theme.colors[name]!, radix: 16)!
        return NSColor(srgbRed: CGFloat((rgb >> 16) & 255) / 255,
                       green: CGFloat((rgb >> 8) & 255) / 255, blue: CGFloat(rgb & 255) / 255, alpha: 1)
    }

    func rebuildControls() {
        buttons.values.forEach { $0.removeFromSuperview() }; buttons.removeAll()
        let dx: CGFloat = settings.compact ? -12 : 0
        let dy: CGFloat = settings.compact ? -54 : 0
        button("hide", "—\n收起", NSRect(x: 63 + dx, y: 194 + dy, width: 70, height: 58), utility: true,
               tip: "暂时收起；下一次中文输入时自动显示")
        button("compact", settings.compact ? "⌃\n展开" : "⌄\n紧凑",
               NSRect(x: 687 + dx, y: 194 + dy, width: 70, height: 58), utility: true,
               tip: settings.compact ? "展开完整界面和底部设置" : "切换紧凑模式，只保留键区")
        if !settings.compact {
            let delay = settings.delay == 0 ? "停顿时保持显示" : "停止 \(Int(settings.delay)) 秒收起"
            button("smaller", "−", NSRect(x: 224, y: 19, width: 28, height: 28), tip: "缩小 5%（最小 50%）")
            button("larger", "+", NSRect(x: 312, y: 19, width: 28, height: 28), tip: "放大 5%（最大 150%，自动适应屏幕空间）")
            button("fainter", "−", NSRect(x: 440, y: 19, width: 28, height: 28), tip: "降低不透明度")
            button("opaque", "+", NSRect(x: 528, y: 19, width: 28, height: 28), tip: "提高不透明度")
            button("pass", passThrough ? "已穿透" : "穿透", NSRect(x: 604, y: 19, width: 56, height: 28),
                   tip: "点击穿过键盘；Control+Option+F9 或菜单栏恢复", active: passThrough ? "initial" : nil)
            button("close", "关闭", NSRect(x: 672, y: 19, width: 56, height: 28),
                   tip: "关闭提示并留在菜单栏，打字不再弹出；Control+Option+F8 或菜单栏恢复")
            button("quit", "退出", NSRect(x: 740, y: 19, width: 56, height: 28), tip: "退出键影，结束程序")
            // Bottom bar, shared with Windows: scheme, theme, delay, position, display, hints, rules.
            button("scheme", "方案：\(guide.scheme.name) ▾", NSRect(x: 24, y: 305, width: 150, height: 28),
                   tip: "快捷切换双拼方案，请与输入法设置保持一致")
            button("theme", "配色：\(theme.name) ▾", NSRect(x: 182, y: 305, width: 126, height: 28), tip: "快捷选择配色主题")
            button("delay", settings.delay == 0 ? "停顿不收起 ▾" : "收起：\(Int(settings.delay)) 秒 ▾",
                   NSRect(x: 316, y: 305, width: 112, height: 28), tip: "设置停止输入后的收起延时")
            button("follow", settings.followInput ? "位置：自动" : "位置：固定", NSRect(x: 436, y: 305, width: 92, height: 28),
                   tip: "自动：跟随输入框，读不到时用最近点击或保存的位置；固定：保持在保存的位置",
                   active: settings.followInput ? "final" : nil)
            button("always", settings.alwaysShow ? "显示：常驻" : "显示：输入时", NSRect(x: 536, y: 305, width: 104, height: 28),
                   tip: "常驻：一直显示键盘；输入时：仅中文输入时显示，\(delay)，任何非字母键收起",
                   active: settings.alwaysShow ? "final" : nil)
            button("hints", settings.hints ? "拼音：开" : "拼音：关", NSRect(x: 648, y: 305, width: 80, height: 28),
                   tip: "下一键韵母提示；英文输入时可关闭。实体按键仍会高亮。", active: settings.hints ? "final" : nil)
            button("rules", showRules ? "收起规则" : "规则", NSRect(x: 736, y: 305, width: 60, height: 28),
                   tip: "显示 / 收起当前双拼方案的规则", active: showRules ? "final" : nil)
        }
        needsDisplay = true
    }

    private func button(_ id: String, _ title: String, _ rect: NSRect, utility: Bool = false,
                        tip: String? = nil, active: String? = nil) {
        let control = OverlayButton(frame: NSRect(x: rect.minX * scale, y: rect.minY * scale,
                                                 width: rect.width * scale, height: rect.height * scale))
        control.title = title
        control.font = .systemFont(ofSize: (utility ? 12 : 11) * scale)
        control.isBordered = false
        control.setButtonType(.momentaryPushIn)
        control.fill = color(utility ? "key" : "button")
        control.hoverFill = color("buttonHover")
        control.ink = color(active ?? (utility ? "muted" : "ink"))
        control.border = utility ? color("keyBorder") : nil
        if utility { control.hoverInk = color("final"); control.hoverBorder = color("final") }
        control.radius = (utility ? 9 : 7) * scale
        control.identifier = NSUserInterfaceItemIdentifier(id)
        control.setAccessibilityLabel(title.replacingOccurrences(of: "\n", with: " "))
        control.toolTip = tip
        control.target = self; control.action = #selector(clicked(_:))
        addSubview(control); buttons[id] = control
    }

    @objc private func clicked(_ sender: NSButton) { onAction?(sender.identifier!.rawValue, sender) }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        context.saveGState(); context.scaleBy(x: scale, y: scale)
        defer { context.restoreGState() }
        round(NSRect(origin: .zero, size: logicalSize).insetBy(dx: 0.5, dy: 0.5), "background", border: "border", radius: 18)
        if !settings.compact {
            logo?.draw(in: NSRect(x: 18, y: 6, width: 52, height: 52), from: .zero,
                       operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
            text("大小", 10, "muted", NSRect(x: 186, y: 19, width: 32, height: 28))
            text("\(Int((scale * 100).rounded()))%", 11, "ink", NSRect(x: 252, y: 19, width: 60, height: 28), center: true)
            text("不透明度", 10, "muted", NSRect(x: 376, y: 19, width: 60, height: 28))
            text("\(Int((settings.opacity * 100).rounded()))%", 11, "ink", NSRect(x: 468, y: 19, width: 60, height: 28), center: true)
        }
        context.saveGState()
        if settings.compact { context.translateBy(x: -12, y: -54) }
        let rows = ["qwertyuiop", guide.scheme.usesSemicolon ? "asdfghjkl;" : "asdfghjkl", "zxcvbnm"]
        let starts: [CGFloat] = [24, guide.scheme.usesSemicolon ? 24 : 63, 141]
        for (row, keys) in rows.enumerated() {
            for (column, char) in keys.enumerated() {
                let key = String(char)
                let rect = NSRect(x: starts[row] + CGFloat(column) * 78, y: 66 + CGFloat(row) * 64, width: 70, height: 58)
                let initial = guide.scheme.initialLabel(key)
                let next = settings.hints && !session.pending.isEmpty && guide.canFollow(session.pending, key)
                let active = flashes[key] != nil
                let fill = active ? "activeKey" : next ? "nextKey" : initial.isEmpty ? "key" : "specialKey"
                let edge = active || next ? "final" : initial.isEmpty ? "keyBorder" : "specialBorder"
                round(rect, fill, border: edge, radius: 9)
                let dim = settings.hints && !session.pending.isEmpty && !next && !active
                text(key.uppercased(), 18, dim ? "muted" : "ink", NSRect(x: rect.minX + 11, y: rect.minY + 4, width: 30, height: 27), bold: true)
                text(initial, 12, "initial", NSRect(x: rect.minX + 39, y: rect.minY + 7, width: 28, height: 21), center: true, bold: true)
                let final = guide.scheme.finalLabel(key)
                text(final, final.count > 8 ? 10 : 12, dim ? "muted" : "final",
                     NSRect(x: rect.minX + 2, y: rect.minY + 31, width: 66, height: 23), center: true)
            }
        }
        context.restoreGState()
        if settings.compact { return }
        round(NSRect(x: 24, y: 264, width: 772, height: 37), "guide", radius: 8)
        var prompt = "按下首键开始  ·  声母 → 韵母"
        if !settings.hints { prompt = "实时按键高亮 · 跟随提示已关闭" }
        else if !session.pending.isEmpty {
            prompt = "\(session.pending.uppercased()) → \(guide.initialLabel(session.pending))   ·   请选择亮框中的韵母键"
        } else if !session.pair.isEmpty {
            let result = guide.decode(session.pair)
            prompt = "\(session.pair.uppercased()) → \(result.isEmpty ? "无对应音节 · Esc 重置" : result + "   ·   继续输入下一音节")"
        }
        if !monitorRunning { prompt = "静态键位预览 · 请从菜单栏「权限设置」启用输入监控" }
        text(prompt, 12, "final", NSRect(x: 36, y: 269, width: 562, height: 25))
        text("声母", 10, "initial", NSRect(x: 602, y: 269, width: 40, height: 25), center: true)
        text("韵母", 10, "final", NSRect(x: 644, y: 269, width: 40, height: 25), center: true)
        text("Esc 重置", 10, "muted", NSRect(x: 704, y: 269, width: 80, height: 25), center: true)
        if showRules {
            for (index, hint) in guide.scheme.hints.enumerated() {
                text(hint, 11, index == 0 ? "ink" : "final", NSRect(x: 25, y: 350 + index * 28, width: 770, height: 27))
            }
        }
    }

    private func round(_ rect: NSRect, _ fill: String, border: String? = nil, radius: CGFloat) {
        let path = NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
        color(fill).setFill(); path.fill()
        if let border { color(border).setStroke(); path.lineWidth = 1; path.stroke() }
    }

    private func text(_ value: String, _ size: CGFloat, _ ink: String, _ rect: NSRect, center: Bool = false, bold: Bool = false) {
        let paragraph = NSMutableParagraphStyle(); paragraph.alignment = center ? .center : .left
        paragraph.lineBreakMode = .byClipping
        let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: size, weight: bold ? .semibold : .regular),
                                                        .foregroundColor: color(ink), .paragraphStyle: paragraph]
        let height = (value as NSString).size(withAttributes: attributes).height
        (value as NSString).draw(in: NSRect(x: rect.minX, y: rect.midY - height / 2, width: rect.width, height: height + 2),
                                withAttributes: attributes)
    }

    override func rightMouseDown(with event: NSEvent) { onContextMenu?(event) }
    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        guard settings.compact || point.y / scale < 60 else { return }
        guard let window else { return }
        onDrag?(window.frame.origin, false)
        window.performDrag(with: event)
    }
}
