import AppKit
import Foundation

// Full-screen overlay shown when the "Display Swap" binding fires. Draws the
// monitor layout to scale and highlights the display(s) the pen just switched to.
//
// Argv: first token = selection ("all" or a 0-based display index); each
// remaining token = a display rect "x,y,w,h" in the virtual-screen space.
// Example: DisplaySwapOverlay 1 0,0,2560,1440 2560,0,1920,1080

struct Display {
    let x: CGFloat, y: CGFloat, w: CGFloat, h: CGFloat
}

final class OverlayController: NSObject, NSApplicationDelegate {
    private let displays: [Display]
    private let selection: String // "all" or an index string
    private var window: NSPanel?

    init(displays: [Display], selection: String) {
        self.displays = displays
        self.selection = selection
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        let screen = NSScreen.main?.frame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)

        // Dimmed full-screen panel so the diagram reads clearly.
        let panel = NSPanel(
            contentRect: screen,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .screenSaver
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]

        let root = NSView(frame: NSRect(origin: .zero, size: screen.size))
        root.wantsLayer = true
        root.layer?.backgroundColor = NSColor(calibratedWhite: 0, alpha: 0.45).cgColor

        // Layout diagram area (centered, ~60% of screen).
        let diagramW = screen.width * 0.6
        let diagramH = screen.height * 0.5
        let diagram = NSView(frame: NSRect(
            x: (screen.width - diagramW) / 2,
            y: (screen.height - diagramH) / 2,
            width: diagramW,
            height: diagramH
        ))
        diagram.wantsLayer = true

        drawDisplays(into: diagram)
        root.addSubview(diagram)

        // Title.
        let title = NSTextField(labelWithString: selection == "all" ? "All Displays" : "Display \((Int(selection) ?? 0) + 1)")
        title.frame = NSRect(x: 0, y: diagram.frame.maxY + 24, width: screen.width, height: 44)
        title.alignment = .center
        title.font = .systemFont(ofSize: 34, weight: .semibold)
        title.textColor = .white
        root.addSubview(title)

        panel.contentView = root
        panel.orderFrontRegardless()
        window = panel

        Timer.scheduledTimer(withTimeInterval: 1.4, repeats: false) { _ in
            panel.orderOut(nil)
            NSApp.terminate(nil)
        }
    }

    private func drawDisplays(into view: NSView) {
        guard !displays.isEmpty else { return }

        // Bounding box of the whole virtual desktop.
        let minX = displays.map { $0.x }.min()!
        let minY = displays.map { $0.y }.min()!
        let maxX = displays.map { $0.x + $0.w }.max()!
        let maxY = displays.map { $0.y + $0.h }.max()!
        let totalW = maxX - minX
        let totalH = maxY - minY

        // Scale to fit the diagram view, keeping aspect ratio, with padding.
        let pad: CGFloat = 8
        let avail = view.bounds.insetBy(dx: pad, dy: pad)
        let scale = min(avail.width / totalW, avail.height / totalH)
        let offX = avail.minX + (avail.width - totalW * scale) / 2
        let offY = avail.minY + (avail.height - totalH * scale) / 2

        let allSelected = selection == "all"
        let selIndex = Int(selection) ?? -1

        for (i, d) in displays.enumerated() {
            // Flip Y: virtual-screen Y grows downward, AppKit grows upward.
            let rx = offX + (d.x - minX) * scale
            let ry = offY + (totalH - (d.y - minY) - d.h) * scale
            let rect = NSRect(x: rx, y: ry, width: d.w * scale, height: d.h * scale).insetBy(dx: 3, dy: 3)

            let selected = allSelected || i == selIndex
            let box = NSView(frame: rect)
            box.wantsLayer = true
            box.layer?.cornerRadius = 8
            box.layer?.borderWidth = 2
            if selected {
                box.layer?.backgroundColor = NSColor.controlAccentColor.withAlphaComponent(0.55).cgColor
                box.layer?.borderColor = NSColor.controlAccentColor.cgColor
            } else {
                box.layer?.backgroundColor = NSColor(calibratedWhite: 1, alpha: 0.08).cgColor
                box.layer?.borderColor = NSColor(calibratedWhite: 1, alpha: 0.25).cgColor
            }

            let num = NSTextField(labelWithString: "\(i + 1)")
            num.frame = NSRect(x: 0, y: rect.height / 2 - 22, width: rect.width, height: 44)
            num.alignment = .center
            num.font = .systemFont(ofSize: min(rect.width, rect.height) * 0.4, weight: .bold)
            num.textColor = selected ? .white : NSColor(calibratedWhite: 1, alpha: 0.5)
            box.addSubview(num)

            view.addSubview(box)
        }
    }
}

func parseArgs() -> (displays: [Display], selection: String) {
    let args = Array(CommandLine.arguments.dropFirst())
    guard let sel = args.first else { return ([], "0") }
    let displays: [Display] = args.dropFirst().compactMap { token in
        let p = token.split(separator: ",").compactMap { Double($0) }
        guard p.count == 4 else { return nil }
        return Display(x: p[0], y: p[1], w: p[2], h: p[3])
    }
    return (displays, sel)
}

let parsed = parseArgs()
let app = NSApplication.shared
let controller = OverlayController(displays: parsed.displays, selection: parsed.selection)
app.delegate = controller
app.run()
