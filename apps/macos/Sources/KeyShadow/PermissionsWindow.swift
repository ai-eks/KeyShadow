import AppKit
import ApplicationServices

final class PermissionsWindow: NSObject {
    private let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 500, height: 300),
                                  styleMask: [.titled, .closable], backing: .buffered, defer: false)
    private let status = NSTextField(wrappingLabelWithString: "")

    override init() {
        super.init()
        window.title = "键影 · 权限设置"; window.isReleasedWhenClosed = false
        let intro = NSTextField(wrappingLabelWithString: "让双拼提示跟上你的输入")
        intro.font = .systemFont(ofSize: 21, weight: .semibold)
        intro.frame = NSRect(x: 28, y: 244, width: 444, height: 32)
        let detail = NSTextField(wrappingLabelWithString: "输入监控用于高亮按键。辅助功能可选，用于识别密码框和定位光标；未授权或无法判断时仍默认高亮，自动位置按最近点击估算。系统安全输入会停止高亮，但未知密码框可能高亮。键影不读取输入文本、不保存历史、不联网。")
        detail.frame = NSRect(x: 28, y: 165, width: 444, height: 68)
        status.frame = NSRect(x: 28, y: 105, width: 444, height: 48)
        for view in [intro, detail, status] { window.contentView?.addSubview(view) }
        for (index, title) in ["开启输入监控", "开启辅助功能", "完成"].enumerated() {
            let button = NSButton(title: title, target: self, action: #selector(choose(_:)))
            button.tag = index; button.bezelStyle = .rounded
            button.frame = NSRect(x: 24 + index * 154, y: 38, width: 144, height: 32)
            window.contentView?.addSubview(button)
        }
    }

    func refresh() {
        status.stringValue = "输入监控：\(CGPreflightListenEventAccess() ? "已授权" : "未授权，不能监听按键")\n辅助功能：\(AXIsProcessTrusted() ? "已授权" : "未授权，默认高亮并按点击估算位置")"
    }

    func show() { refresh(); window.center(); NSApp.activate(ignoringOtherApps: true); window.makeKeyAndOrderFront(nil) }

    @objc private func choose(_ sender: NSButton) {
        switch sender.tag {
        case 0:
            if !CGRequestListenEventAccess() { openSettings("Privacy_ListenEvent") }
        case 1:
            let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
            if !AXIsProcessTrustedWithOptions(options) { openSettings("Privacy_Accessibility") }
        default: window.orderOut(nil)
        }
        refresh()
    }

    private func openSettings(_ pane: String) {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane)")!)
    }
}
