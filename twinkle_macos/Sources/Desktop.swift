import AppKit
import ImageIO
import UniformTypeIdentifiers

final class Clip {
    let frames: [NSImage]
    let durations: [Double]
    let normalize: Bool

    init(files: [String], durations: [Double], root: URL, normalize: Bool = false) throws {
        guard !files.isEmpty, files.count == durations.count else {
            throw ResourceError.invalid("Invalid animation manifest")
        }
        self.frames = try files.map { path in
            let url = try safeResource(path, root: root)
            guard let image = NSImage(contentsOf: url) else { throw ResourceError.invalid(path) }
            return image
        }
        self.durations = durations
        self.normalize = normalize
    }

    init(imported url: URL) throws {
        let size = (try url.resourceValues(forKeys: [.fileSizeKey])).fileSize ?? 0
        guard size > 0, size <= 32 * 1024 * 1024,
              let source = CGImageSourceCreateWithURL(url as CFURL, nil) else {
            throw ResourceError.invalid("Choose an image smaller than 32 MB.")
        }
        let count = CGImageSourceGetCount(source)
        guard (1...300).contains(count) else { throw ResourceError.invalid("Maximum: 300 frames") }
        var images: [NSImage] = [], times: [Double] = []
        for index in 0..<count {
            let options: [CFString: Any] = [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceThumbnailMaxPixelSize: 512,
                kCGImageSourceCreateThumbnailWithTransform: true
            ]
            guard let image = CGImageSourceCreateThumbnailAtIndex(source, index, options as CFDictionary) else {
                throw ResourceError.invalid("Unable to decode image frame")
            }
            images.append(NSImage(cgImage: image, size: NSSize(width: CGFloat(image.width), height: CGFloat(image.height))))
            let props = CGImageSourceCopyPropertiesAtIndex(source, index, nil) as? [String: Any] ?? [:]
            let animation = (props[kCGImagePropertyGIFDictionary as String] as? [String: Any])
                ?? (props[kCGImagePropertyPNGDictionary as String] as? [String: Any]) ?? [:]
            let seconds = (animation["UnclampedDelayTime"] as? Double)
                ?? (animation["DelayTime"] as? Double) ?? 0.1
            times.append(seconds.isFinite ? max(20, seconds * 1000) : 100)
        }
        frames = images; durations = times; normalize = true
    }

    func image(at age: Double) -> NSImage { frames[Playback.frame(at: age, durations: durations)] }
}

final class PetPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class PetView: NSView {
    var image: NSImage?
    var normalize = false
    var mirrored = false
    var onClick: ((NSEvent) -> Void)?
    var onResize: ((Double) -> Void)?
    var onDragEnd: (() -> Void)?
    var onInteraction: (() -> Void)?
    private(set) var dragging = false
    private var initialPoint = NSPoint.zero
    private var initialOrigin = NSPoint.zero

    override func draw(_ dirtyRect: NSRect) {
        guard let image = image else { return }
        NSGraphicsContext.current?.imageInterpolation = .high
        var box = bounds
        if normalize {
            box = NSRect(x: bounds.width * 0.0625, y: bounds.height * 0.0625,
                         width: bounds.width * 0.875, height: bounds.height * 0.875)
        }
        let scale = min(box.width / max(1, image.size.width), box.height / max(1, image.size.height))
        let size = NSSize(width: image.size.width * scale, height: image.size.height * scale)
        let rect = NSRect(x: box.midX - size.width / 2,
                          y: normalize ? box.minY : box.midY - size.height / 2,
                          width: size.width, height: size.height)
        NSGraphicsContext.saveGraphicsState()
        if mirrored {
            let transform = NSAffineTransform()
            transform.translateX(by: bounds.width, yBy: 0); transform.scaleX(by: -1, yBy: 1)
            transform.concat()
        }
        image.draw(in: rect, from: .zero, operation: .sourceOver, fraction: 1,
                   respectFlipped: false, hints: nil)
        NSGraphicsContext.restoreGraphicsState()
    }

    override func mouseDown(with event: NSEvent) {
        initialPoint = NSEvent.mouseLocation
        initialOrigin = window?.frame.origin ?? .zero
        dragging = false; onInteraction?()
    }
    override func mouseDragged(with event: NSEvent) {
        let point = NSEvent.mouseLocation
        let dx = point.x - initialPoint.x, dy = point.y - initialPoint.y
        if abs(dx) + abs(dy) > 4 { dragging = true }
        if dragging { window?.setFrameOrigin(NSPoint(x: initialOrigin.x + dx, y: initialOrigin.y + dy)) }
    }
    override func mouseUp(with event: NSEvent) {
        if dragging { onDragEnd?() } else { onClick?(event) }
        dragging = false
    }
    override func rightMouseDown(with event: NSEvent) { onClick?(event) }
    override func scrollWheel(with event: NSEvent) {
        if event.modifierFlags.contains(.control) || event.modifierFlags.contains(.command) {
            onResize?(event.scrollingDeltaY > 0 ? 8 : -8)
        } else { super.scrollWheel(with: event) }
    }
}

final class Desktop: NSObject, NSApplicationDelegate, NSMenuDelegate {
    let root: URL
    let settingsURL: URL
    var preferences: Preferences
    var defaults: [String: Clip] = [:]
    var library: [LibraryItem] = []
    var cache: [String: Clip] = [:]
    var session = Session()
    var activity = Activity.offWork
    let panel: PetPanel
    let pet = PetView()
    var statusItem: NSStatusItem?
    var timer: Timer?
    var currentState = "idle"
    var stateStarted: Double = 0
    var pausedAge: Double = 0
    var lastTick: Double = 0
    var nextWalk: Double = 0
    var destination: Double?
    var farewellUntil: Double = 0
    var interacting = false
    var isMenuOpen = false
    var picker: NSWindow?
    var pickerActivity = Activity.offWork
    var smokeTest = false
    var assistantBusy = false
    var assistant: AssistantWindow?

    var now: Double { ProcessInfo.processInfo.systemUptime }
    func text(_ ko: String, _ en: String) -> String { preferences.english ? en : ko }

    init(root: URL, settingsURL: URL? = nil) throws {
        self.root = root
        self.settingsURL = settingsURL ?? Preferences.directory.appendingPathComponent("settings.json")
        preferences = Preferences.load(from: self.settingsURL)
        panel = PetPanel(contentRect: NSRect(x: 0, y: 0, width: preferences.size, height: preferences.size),
                         styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        super.init()
        let decoder = JSONDecoder()
        let specs = try decoder.decode([String: AnimationSpec].self,
                                       from: Data(contentsOf: root.appendingPathComponent("assets/animations.json")))
        for key in ["idle", "focus", "rest", "meeting", "walk_left", "walk_right", "reaction", "farewell"] {
            guard let spec = specs[key] else { throw ResourceError.invalid("Missing animation: \(key)") }
            defaults[key] = try Clip(files: spec.files, durations: spec.durations_ms, root: root)
        }
        library = try decoder.decode([LibraryItem].self,
                                    from: Data(contentsOf: root.appendingPathComponent("library/catalog.json")))
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        panel.isOpaque = false; panel.backgroundColor = .clear; panel.hasShadow = false
        panel.level = .floating; panel.isFloatingPanel = true; panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = pet
        pet.frame = NSRect(origin: .zero, size: panel.frame.size)
        pet.autoresizingMask = [.width, .height]
        pet.onClick = { [weak self] event in self?.showMenu(event) }
        pet.onInteraction = { [weak self] in self?.destination = nil; self?.nextWalk = (self?.now ?? 0) + 5 }
        pet.onDragEnd = { [weak self] in self?.clampToScreen() }
        pet.onResize = { [weak self] delta in
            guard let self = self else { return }; self.resize(self.preferences.size + delta)
        }
        let area = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1024, height: 768)
        panel.setFrameOrigin(NSPoint(x: area.maxX - panel.frame.width - 40, y: area.minY + 32))
        panel.orderFrontRegardless()
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.title = "✦"; item.button?.toolTip = "Twinkle Agent"
        item.menu = makeMenu(); statusItem = item
        stateStarted = now; lastTick = now; nextWalk = now + 3
        let tickTimer = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(tickTimer, forMode: .common); timer = tickTimer
        NotificationCenter.default.addObserver(self, selector: #selector(screenChanged),
            name: NSApplication.didChangeScreenParametersNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(woke),
            name: NSWorkspace.didWakeNotification, object: nil)
        tick()
        let agentSettings = settingsURL.deletingLastPathComponent().appendingPathComponent("assistant.json")
        if !smokeTest && AssistantSettings.load(agentSettings).monitorMail { assistant = AssistantWindow(desktop: self) }
        if smokeTest { runSmokeTest() }
    }

    func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate()
        assistant?.stop()
        if smokeTest { try? FileManager.default.removeItem(at: settingsURL.deletingLastPathComponent()) }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        panel.orderFrontRegardless(); return false
    }

    func clip(for state: String) -> (Clip, Bool) {
        let mode = ["focus": Activity.focus, "rest": .rest, "meeting": .meeting][state] ?? .offWork
        if state != "farewell", let id = preferences.choices[mode.rawValue] {
            if let loaded = cache[id] { return (loaded, state == "walk_left") }
            do {
                let loaded: Clip
                if id.hasPrefix("import-") {
                    let imported = try safeResource(id, root: settingsURL.deletingLastPathComponent().appendingPathComponent("images"))
                    loaded = try Clip(imported: imported)
                } else if let item = library.first(where: { $0.Id == id }) {
                    loaded = try Clip(files: item.files, durations: item.durations_ms, root: root, normalize: true)
                } else { throw ResourceError.invalid("Unknown image") }
                cache[id] = loaded; return (loaded, state == "walk_left")
            } catch {
                preferences.choices.removeValue(forKey: mode.rawValue); save()
            }
        }
        return (defaults[state]!, false)
    }

    func tick() {
        let time = now, dt = min(0.1, max(0, time - lastTick)); lastTick = time
        var state = activity.animation
        if assistantBusy && !session.active { state = "focus" }
        let canWalk = activity == .offWork && preferences.roaming && panel.isVisible
            && !pet.dragging && !interacting && !isMenuOpen && !assistantBusy && time >= farewellUntil
        if time < farewellUntil { state = "farewell" }
        if canWalk {
            let area = (panel.screen ?? NSScreen.main)?.visibleFrame ?? panel.frame
            if destination == nil && time >= nextWalk {
                let upper = max(area.minX, area.maxX - panel.frame.width)
                destination = Double.random(in: Double(area.minX)...Double(upper))
            }
            if let target = destination {
                let x = Double(panel.frame.minX)
                let updated = Motion.approach(x, target: target, speed: 42, dt: dt)
                panel.setFrameOrigin(NSPoint(x: updated, y: panel.frame.minY))
                state = updated >= x ? "walk_right" : "walk_left"
                if abs(updated - target) < 0.5 { destination = nil; nextWalk = time + Double.random(in: 4...9) }
            }
        } else { destination = nil }
        if state != currentState { currentState = state; stateStarted = time }
        let (clip, mirror) = clip(for: state)
        let age = session.paused ? pausedAge : (time - stateStarted) * 1000
        pet.image = clip.image(at: age); pet.normalize = clip.normalize; pet.mirrored = mirror
        pet.needsDisplay = true
        statusItem?.button?.title = session.active ? "✦ " + Session.text(session.elapsed(at: time)) : "✦"
        statusItem?.button?.toolTip = "Twinkle Agent · " + activity.title(preferences.english)
    }

    func switchActivity(_ value: Activity) {
        activity = value; destination = nil; pausedAge = 0; stateStarted = now
        if value == .offWork {
            session.stop(); preferences.roaming = true
            farewellUntil = now + 3; nextWalk = farewellUntil + 0.4
        } else { session.start(at: now); farewellUntil = 0 }
        save(); tick(); refreshMenu()
    }

    @objc func selectActivity(_ sender: NSMenuItem) {
        if let raw = sender.representedObject as? String, let value = Activity(rawValue: raw) { switchActivity(value) }
    }
    @objc func pauseSession() {
        if !session.paused { pausedAge = (now - stateStarted) * 1000 }
        else { stateStarted = now - pausedAge / 1000 }
        session.togglePause(at: now); refreshMenu()
    }
    @objc func toggleRoaming() {
        preferences.roaming.toggle(); destination = nil; nextWalk = now + 1; save(); refreshMenu()
    }
    @objc func toggleVisibility() {
        if panel.isVisible { panel.orderOut(nil) } else { panel.orderFrontRegardless(); clampToScreen() }
        refreshMenu()
    }
    @objc func resizeFromMenu(_ sender: NSMenuItem) { resize(preferences.size + Double(sender.tag)) }
    func resize(_ value: Double) {
        preferences.size = Preferences.normalizedSize(value)
        panel.setContentSize(NSSize(width: preferences.size, height: preferences.size))
        clampToScreen(); save(); refreshMenu()
    }
    func save() {
        do { try preferences.save(to: settingsURL) }
        catch { NSLog("Twinkle Agent: could not save preferences") }
    }
    @objc func screenChanged() { destination = nil; clampToScreen() }
    @objc func woke() { lastTick = now; destination = nil; clampToScreen() }
    func clampToScreen() {
        let center = NSPoint(x: panel.frame.midX, y: panel.frame.midY)
        let screen = NSScreen.screens.first(where: { $0.frame.contains(center) }) ?? panel.screen ?? NSScreen.main
        guard let area = screen?.visibleFrame else { return }
        panel.setFrameOrigin(NSPoint(
            x: Motion.clamp(Double(panel.frame.minX), min: Double(area.minX), max: Double(area.maxX - panel.frame.width)),
            y: Motion.clamp(Double(panel.frame.minY), min: Double(area.minY), max: Double(area.maxY - panel.frame.height))))
    }

    func menuWillOpen(_ menu: NSMenu) {
        isMenuOpen = true
        if menu === statusItem?.menu { populate(menu) }
    }
    func menuDidClose(_ menu: NSMenu) { isMenuOpen = false; nextWalk = now + 2 }
    func makeMenu() -> NSMenu { let menu = NSMenu(); menu.delegate = self; populate(menu); return menu }
    func refreshMenu() { if !isMenuOpen { statusItem?.menu = makeMenu() } }
    @discardableResult
    func add(_ menu: NSMenu, _ title: String, _ action: Selector, value: Any? = nil) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self; item.representedObject = value; menu.addItem(item); return item
    }
    func populate(_ menu: NSMenu) {
        menu.removeAllItems()
        let heading = NSMenuItem(title: "Twinkle Agent ✦", action: nil, keyEquivalent: "")
        heading.isEnabled = false; menu.addItem(heading)
        if session.active {
            let clock = NSMenuItem(title: activity.title(preferences.english) + " · " + Session.text(session.elapsed(at: now)),
                                   action: nil, keyEquivalent: "")
            clock.isEnabled = false; menu.addItem(clock)
        }
        menu.addItem(.separator())
        for value in Activity.allCases {
            let item = add(menu, value.title(preferences.english), #selector(selectActivity(_:)), value: value.rawValue)
            item.state = activity == value ? .on : .off
        }
        if session.active {
            add(menu, session.paused ? text("다시 시작", "Resume") : text("일시정지", "Pause"), #selector(pauseSession))
        }
        menu.addItem(.separator())
        let roaming = add(menu, text("자유롭게 돌아다니기", "Roam around"), #selector(toggleRoaming))
        roaming.state = preferences.roaming ? .on : .off
        let size = NSMenuItem(title: text("크기", "Size") + " · \(Int(preferences.size)) px", action: nil, keyEquivalent: "")
        let sizes = NSMenu()
        add(sizes, text("작게 −8 px", "Smaller −8 px"), #selector(resizeFromMenu(_:))).tag = -8
        add(sizes, text("크게 +8 px", "Larger +8 px"), #selector(resizeFromMenu(_:))).tag = 8
        add(sizes, text("기본 크기 144 px", "Reset to 144 px"), #selector(resetSize))
        size.submenu = sizes; menu.addItem(size)
        let images = NSMenuItem(title: text("상황별 이미지 바꾸기", "Choose activity images"), action: nil, keyEquivalent: "")
        let choices = NSMenu()
        for value in Activity.allCases { add(choices, value.title(preferences.english), #selector(openPicker(_:)), value: value.rawValue) }
        images.submenu = choices; menu.addItem(images)
        menu.addItem(.separator())
        add(menu, text("메일 · AI 도우미", "Mail & AI assistant"), #selector(openAssistant))
        add(menu, "한국어 / English", #selector(toggleLanguage))
        add(menu, "사용법 / Guide (KOR · ENG)", #selector(openGuide))
        add(menu, text("이미지 갤러리", "Image gallery"), #selector(openGallery))
        add(menu, text("이 Mac 버전 정보", "About this Mac version"), #selector(about))
        menu.addItem(.separator())
        add(menu, panel.isVisible ? text("숨기기", "Hide") : text("트윙클 보이기", "Show Twinkle"), #selector(toggleVisibility))
        add(menu, text("종료", "Quit"), #selector(quit))
    }
    func showMenu(_ event: NSEvent) { NSMenu.popUpContextMenu(makeMenu(), with: event, for: pet) }
    @objc func resetSize() { resize(144) }
    @objc func toggleLanguage() { preferences.english.toggle(); save(); refreshMenu() }
    @objc func openGuide() { NSWorkspace.shared.open(root.appendingPathComponent("guide/index.html")) }
    @objc func openGallery() { NSWorkspace.shared.open(root.appendingPathComponent("gallery/index.html")) }
    @objc func quit() { NSApplication.shared.terminate(nil) }
    @objc func openAssistant() {
        if assistant == nil { assistant = AssistantWindow(desktop: self) }
        assistant?.show()
    }
    @objc func about() {
        let alert = NSAlert(); alert.messageText = "Twinkle Agent · macOS 0.10.0 preview"
        alert.informativeText = text(
            "macOS 12 이상 · Apple Silicon / Intel\n캐릭터 이동·타이머·47개 이미지·개인 이미지 추가를 지원해요.\nMail 앱의 메일과 ChatGPT 구독 / Claude Code를 연결해 요약과 할 일을 정리해요. 선택한 메일은 해당 AI 서비스로 전송됩니다.\n브라우저 완료 알림은 아직 Windows 전용입니다.",
            "macOS 12+ · Apple Silicon / Intel\nRoaming, timers, 47 animations, and image import.\nConnect Mail with a ChatGPT plan or Claude Code for summaries and next actions. Selected messages are sent to the chosen AI service.\nThe browser completion bridge remains Windows-only.")
        NSApplication.shared.activate(ignoringOtherApps: true); alert.runModal()
    }
}
