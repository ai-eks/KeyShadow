import AppKit
import WebKit

/// Test-only UI protocol: integer commands select known blank controls. No arbitrary
/// script, text, external URL, input monitoring or accessibility permission is used.
final class PrivacyFixture {
    let window = NSWindow(contentRect: NSRect(x: 200, y: 200, width: 520, height: 300),
                          styleMask: [.titled, .closable], backing: .buffered, defer: false)
    let normal = NSTextField(string: ""), password = NSSecureTextField(string: "")
    let readOnly = NSTextView(frame: NSRect(x: 20, y: 152, width: 460, height: 26))
    let web: WKWebView
    var timer: Timer?
    var lastCommand = ""

    init(directory: URL) {
        let config = WKWebViewConfiguration(); config.websiteDataStore = .nonPersistent()
        web = WKWebView(frame: NSRect(x: 20, y: 20, width: 460, height: 130), configuration: config)
        window.title = "键影 · 空白输入框隐私测试"; window.isReleasedWhenClosed = false
        for (i, field) in [normal, password].enumerated() {
            field.frame = NSRect(x: 20, y: 230 - i * 50, width: 460, height: 32)
            window.contentView?.addSubview(field)
        }
        readOnly.isEditable = false; readOnly.isSelectable = true
        window.contentView?.addSubview(readOnly)
        window.contentView?.addSubview(web)
        web.loadHTMLString("<input id='normal'><input id='password' type='password'><textarea id='area'></textarea>", baseURL: nil)
        NSApp.setActivationPolicy(.regular)
        window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        timer = Timer.scheduledTimer(withTimeInterval: 0.03, repeats: true) { [weak self] _ in
            guard let self, !self.web.isLoading,
                  let command = try? String(contentsOf: directory.appendingPathComponent("command"), encoding: .utf8),
                  command != self.lastCommand,
                  let mode = Int(command.split(separator: ":").last ?? ""), (0...9).contains(mode) else { return }
            self.lastCommand = command
            let ready = {
                // WebKit propagates DOM focus to its accessibility process asynchronously.
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
                    try? command.write(to: directory.appendingPathComponent("ready"), atomically: true, encoding: .utf8)
                }
            }
            if mode < 3 || mode == 9 {
                switch mode {
                case 1: self.window.makeFirstResponder(self.password)
                case 2: self.window.makeFirstResponder(self.readOnly)
                case 9: self.window.makeFirstResponder(nil)
                default: self.window.makeFirstResponder(self.normal)
                }
                ready()
            } else {
                self.window.makeFirstResponder(self.web)
                let scripts = [
                    "document.getElementById('normal').focus()",
                    "document.getElementById('password').focus()",
                    "document.getElementById('normal').focus(); document.getElementById('normal').type='password'",
                    "document.getElementById('normal').type='text'",
                    "document.getElementById('area').focus()",
                    "document.getElementById('area').readOnly=true"
                ]
                self.web.evaluateJavaScript(scripts[mode - 3]) { _, error in if error == nil { ready() } }
            }
        }
    }
}

final class FixtureDelegate: NSObject, NSApplicationDelegate {
    var fixture: PrivacyFixture?
    func applicationDidFinishLaunching(_ notification: Notification) {
        guard CommandLine.arguments.count == 3, CommandLine.arguments[1] == "--privacy-fixture" else { exit(1) }
        fixture = PrivacyFixture(directory: URL(fileURLWithPath: CommandLine.arguments[2]))
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.regular)
let delegate = FixtureDelegate()
app.delegate = delegate
app.run()
