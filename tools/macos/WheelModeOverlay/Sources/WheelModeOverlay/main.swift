import AppKit
import Foundation

final class OverlayController: NSObject, NSApplicationDelegate {
    private let mode: String
    private var window: NSPanel?

    init(mode: String) {
        self.mode = mode
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        let screenFrame = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 800, height: 600)
        let width: CGFloat = 380
        let height: CGFloat = 104
        let rect = NSRect(
            x: screenFrame.midX - width / 2,
            y: screenFrame.maxY - height - 90,
            width: width,
            height: height
        )

        let panel = NSPanel(
            contentRect: rect,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .screenSaver
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]

        let container = NSView(frame: NSRect(x: 0, y: 0, width: width, height: height))
        container.wantsLayer = true
        container.layer?.backgroundColor = NSColor(calibratedWhite: 0.05, alpha: 0.88).cgColor
        container.layer?.cornerRadius = 18

        let title = NSTextField(labelWithString: "Wheel Mode")
        title.frame = NSRect(x: 0, y: 64, width: width, height: 22)
        title.alignment = .center
        title.font = .systemFont(ofSize: 14, weight: .medium)
        title.textColor = NSColor(calibratedWhite: 0.78, alpha: 1)

        let value = NSTextField(labelWithString: mode)
        value.frame = NSRect(x: 0, y: 23, width: width, height: 40)
        value.alignment = .center
        value.font = .systemFont(ofSize: 30, weight: .semibold)
        value.textColor = .white

        container.addSubview(title)
        container.addSubview(value)
        panel.contentView = container
        panel.orderFrontRegardless()

        window = panel
        Timer.scheduledTimer(withTimeInterval: 2, repeats: false) { _ in
            panel.orderOut(nil)
            NSApp.terminate(nil)
        }
    }
}

let mode = CommandLine.arguments.dropFirst().joined(separator: " ")
let app = NSApplication.shared
let controller = OverlayController(mode: mode.isEmpty ? "Scroll" : mode)
app.delegate = controller
app.run()
