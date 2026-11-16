// DualCursor — her ekrana ayrı bir imleç.
// Gerçek imleç aktif ekrana kilitlenir; diğer ekranlarda "hayalet" imleç
// en son bırakılan yerde durur. Kısayol tuşuyla anında yer değiştirirler.
// MIT License

import AppKit
import Carbon.HIToolbox

// MARK: - Ayarlar (~/.config/dualcursor/config.json)

struct HotkeyConfig: Codable {
    var key: String = "space"
    var modifiers: [String] = ["ctrl", "alt"]
}

let configURL = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent(".config/dualcursor/config.json")

func loadConfig() -> HotkeyConfig {
    if let data = try? Data(contentsOf: configURL),
       let cfg = try? JSONDecoder().decode(HotkeyConfig.self, from: data) {
        return cfg
    }
    let cfg = HotkeyConfig()
    try? FileManager.default.createDirectory(at: configURL.deletingLastPathComponent(),
                                             withIntermediateDirectories: true)
    let enc = JSONEncoder()
    enc.outputFormatting = .prettyPrinted
    try? enc.encode(cfg).write(to: configURL)
    return cfg
}

let keyCodes: [String: Int] = [
    "a": kVK_ANSI_A, "b": kVK_ANSI_B, "c": kVK_ANSI_C, "d": kVK_ANSI_D, "e": kVK_ANSI_E,
    "f": kVK_ANSI_F, "g": kVK_ANSI_G, "h": kVK_ANSI_H, "i": kVK_ANSI_I, "j": kVK_ANSI_J,
    "k": kVK_ANSI_K, "l": kVK_ANSI_L, "m": kVK_ANSI_M, "n": kVK_ANSI_N, "o": kVK_ANSI_O,
    "p": kVK_ANSI_P, "q": kVK_ANSI_Q, "r": kVK_ANSI_R, "s": kVK_ANSI_S, "t": kVK_ANSI_T,
    "u": kVK_ANSI_U, "v": kVK_ANSI_V, "w": kVK_ANSI_W, "x": kVK_ANSI_X, "y": kVK_ANSI_Y,
    "z": kVK_ANSI_Z,
    "0": kVK_ANSI_0, "1": kVK_ANSI_1, "2": kVK_ANSI_2, "3": kVK_ANSI_3, "4": kVK_ANSI_4,
    "5": kVK_ANSI_5, "6": kVK_ANSI_6, "7": kVK_ANSI_7, "8": kVK_ANSI_8, "9": kVK_ANSI_9,
    "space": kVK_Space, "tab": kVK_Tab, "return": kVK_Return, "escape": kVK_Escape,
    "grave": kVK_ANSI_Grave,
    "f1": kVK_F1, "f2": kVK_F2, "f3": kVK_F3, "f4": kVK_F4, "f5": kVK_F5, "f6": kVK_F6,
    "f7": kVK_F7, "f8": kVK_F8, "f9": kVK_F9, "f10": kVK_F10, "f11": kVK_F11, "f12": kVK_F12,
    "f13": kVK_F13, "f14": kVK_F14, "f15": kVK_F15, "f16": kVK_F16, "f17": kVK_F17,
    "f18": kVK_F18, "f19": kVK_F19, "f20": kVK_F20,
]

let modifierFlags: [String: Int] = [
    "cmd": cmdKey, "command": cmdKey,
    "alt": optionKey, "option": optionKey,
    "ctrl": controlKey, "control": controlKey,
    "shift": shiftKey,
]

// MARK: - Hayalet imleç görünümü

final class GhostView: NSView {
    /// Ekranın sol-üst köşesine göre konum (nil = gizli)
    var point: CGPoint?

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.clear.set()
        dirtyRect.fill()
        guard let p = point else { return }
        let cursor = NSCursor.arrow
        let img = cursor.image
        let hot = cursor.hotSpot
        let rect = NSRect(x: p.x - hot.x, y: p.y - hot.y,
                          width: img.size.width, height: img.size.height)
        img.draw(in: rect, from: .zero, operation: .sourceOver,
                 fraction: 0.8, respectFlipped: true, hints: nil)
    }
}

struct ScreenInfo {
    let displayID: CGDirectDisplayID
    let cgBounds: CGRect        // global, sol-üst köşe orijinli
    let window: NSWindow
    let view: GhostView
}

// MARK: - Ana kontrolcü

final class DualCursor {
    var screens: [ScreenInfo] = []
    var saved: [CGDirectDisplayID: CGPoint] = [:]
    var activeID: CGDirectDisplayID = 0
    var tap: CFMachPort?
    var hotKeyRef: EventHotKeyRef?
    var hotkeyLabel = ""

    // Ekranlar

    func rebuildScreens() {
        screens.forEach { $0.window.orderOut(nil) }
        screens.removeAll()

        for screen in NSScreen.screens {
            guard let num = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber
            else { continue }
            let id = CGDirectDisplayID(num.uint32Value)

            let w = NSWindow(contentRect: screen.frame, styleMask: .borderless,
                             backing: .buffered, defer: false)
            w.setFrame(screen.frame, display: false)
            w.isOpaque = false
            w.backgroundColor = .clear
            w.hasShadow = false
            w.ignoresMouseEvents = true
            w.level = .screenSaver
            w.isReleasedWhenClosed = false
            w.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]

            let v = GhostView(frame: NSRect(origin: .zero, size: screen.frame.size))
            w.contentView = v
            w.orderFrontRegardless()

            screens.append(ScreenInfo(displayID: id, cgBounds: CGDisplayBounds(id), window: w, view: v))
        }

        // Eskimiş ekran kayıtlarını at, yenilere orta nokta ver
        let ids = Set(screens.map { $0.displayID })
        saved = saved.filter { ids.contains($0.key) }
        for s in screens where saved[s.displayID] == nil {
            saved[s.displayID] = CGPoint(x: s.cgBounds.midX, y: s.cgBounds.midY)
        }

        let cur = CGEvent(source: nil)?.location ?? .zero
        activeID = screens.first(where: { $0.cgBounds.contains(cur) })?.displayID
            ?? screens.first?.displayID ?? 0
        refreshGhosts()
    }

    func refreshGhosts() {
        for s in screens {
            if s.displayID == activeID {
                s.view.point = nil
            } else if let p = saved[s.displayID] {
                s.view.point = CGPoint(x: p.x - s.cgBounds.minX, y: p.y - s.cgBounds.minY)
            }
            s.view.needsDisplay = true
        }
    }

    /// İmleç aktif ekranın dışına çıktıysa kenara sabitlenmiş konumu döndürür.
    func clamp(_ p: CGPoint) -> CGPoint? {
        guard let b = screens.first(where: { $0.displayID == activeID })?.cgBounds,
              !b.contains(p) else { return nil }
        return CGPoint(x: min(max(p.x, b.minX), b.maxX - 1),
                       y: min(max(p.y, b.minY), b.maxY - 1))
    }

    // Geçiş

    func switchNext() {
        guard screens.count > 1 else { return }
        let cur = CGEvent(source: nil)?.location ?? .zero
        saved[activeID] = cur

        let idx = screens.firstIndex(where: { $0.displayID == activeID }) ?? 0
        let next = screens[(idx + 1) % screens.count]
        activeID = next.displayID

        let target = saved[next.displayID] ?? CGPoint(x: next.cgBounds.midX, y: next.cgBounds.midY)
        CGWarpMouseCursorPosition(target)
        CGAssociateMouseAndMouseCursorPosition(1)
        // Uygulamaların hover durumunu güncellemesi için sahte bir hareket olayı
        CGEvent(mouseEventSource: nil, mouseType: .mouseMoved,
                mouseCursorPosition: target, mouseButton: .left)?.post(tap: .cghidEventTap)
        refreshGhosts()
    }

    // Fare kilidi (event tap — Erişilebilirlik izni gerekir)

    @discardableResult
    func installTap() -> Bool {
        if tap != nil { return true }
        let types: [CGEventType] = [.mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged]
        let mask = types.reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }

        let callback: CGEventTapCallBack = { _, type, event, refcon in
            guard let refcon else { return Unmanaged.passUnretained(event) }
            let me = Unmanaged<DualCursor>.fromOpaque(refcon).takeUnretainedValue()

            if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
                if let t = me.tap { CGEvent.tapEnable(tap: t, enable: true) }
                return Unmanaged.passUnretained(event)
            }
            if let c = me.clamp(event.location) {
                event.location = c
                CGWarpMouseCursorPosition(c)
                CGAssociateMouseAndMouseCursorPosition(1)
            }
            return Unmanaged.passUnretained(event)
        }

        guard let t = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap,
                                        options: .defaultTap, eventsOfInterest: mask,
                                        callback: callback,
                                        userInfo: Unmanaged.passUnretained(self).toOpaque())
        else { return false }

        tap = t
        let src = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, t, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), src, .commonModes)
        CGEvent.tapEnable(tap: t, enable: true)
        return true
    }

    // Global kısayol (Carbon — ek izin gerektirmez)

    func installHotkey(_ cfg: HotkeyConfig) {
        guard let code = keyCodes[cfg.key.lowercased()] else {
            NSLog("DualCursor: bilinmeyen tuş '\(cfg.key)', 'space' kullanılıyor")
            return installHotkey(HotkeyConfig(key: "space", modifiers: cfg.modifiers))
        }
        let mods = cfg.modifiers.reduce(0) { $0 | (modifierFlags[$1.lowercased()] ?? 0) }
        hotkeyLabel = (cfg.modifiers + [cfg.key]).joined(separator: "+")

        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard),
                                 eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, userData -> OSStatus in
            guard let userData else { return noErr }
            let me = Unmanaged<DualCursor>.fromOpaque(userData).takeUnretainedValue()
            DispatchQueue.main.async { me.switchNext() }
            return noErr
        }, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), nil)

        let hkID = EventHotKeyID(signature: OSType(0x4443_5352), id: 1) // "DCSR"
        RegisterEventHotKey(UInt32(code), UInt32(mods), hkID,
                            GetApplicationEventTarget(), 0, &hotKeyRef)
    }
}

// MARK: - Uygulama

final class AppDelegate: NSObject, NSApplicationDelegate {
    let dc = DualCursor()
    var statusItem: NSStatusItem!
    var retryTimer: Timer?

    func applicationDidFinishLaunching(_ note: Notification) {
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(opts)

        dc.rebuildScreens()
        dc.installHotkey(loadConfig())

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "Geçiş: \(dc.hotkeyLabel)", action: nil, keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Ayar dosyasını aç", action: #selector(openConfig), keyEquivalent: ""))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Çıkış", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        menu.items.forEach { if $0.action == #selector(openConfig) { $0.target = self } }
        statusItem.menu = menu

        // İzin verilene kadar event tap'i kurmayı dene
        updateStatus()
        if !dc.installTap() {
            retryTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] t in
                guard let self else { return }
                if self.dc.installTap() { t.invalidate(); self.updateStatus() }
            }
        }

        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in
            self?.dc.rebuildScreens()
        }
    }

    func updateStatus() {
        statusItem.button?.title = dc.tap == nil ? "⇄ !" : "⇄"
        statusItem.button?.toolTip = dc.tap == nil
            ? "Erişilebilirlik izni bekleniyor"
            : "DualCursor — \(dc.hotkeyLabel)"
    }

    @objc func openConfig() {
        NSWorkspace.shared.open(configURL)
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let delegate = AppDelegate()
app.delegate = delegate
app.run()
