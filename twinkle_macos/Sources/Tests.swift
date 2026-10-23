import AppKit

private func check(_ condition: @autoclosure () -> Bool, _ message: String) throws {
    if !condition() { throw ResourceError.invalid("TEST FAILED: " + message) }
}

func runSelfTests(root: URL) throws {
    var session = Session()
    session.start(at: 10)
    try check(session.elapsed(at: 15) == 5, "elapsed time")
    session.togglePause(at: 15)
    try check(session.elapsed(at: 500) == 5, "pause excludes time")
    session.togglePause(at: 500)
    try check(session.elapsed(at: 503) == 8, "resume preserves elapsed time")
    session.stop()
    try check(!session.active && session.elapsed(at: 600) == 0, "stop resets timer")
    try check(Session.text(3661) == "01:01:01", "long timer formatting")
    try check(Playback.frame(at: 99, durations: [100, 200]) == 0, "first frame")
    try check(Playback.frame(at: 100, durations: [100, 200]) == 1, "frame boundary")
    try check(Playback.frame(at: 300, durations: [100, 200]) == 0, "loop boundary")
    try check(Preferences.normalizedSize(.nan) == 144, "invalid size")
    try check(Preferences.normalizedSize(1000) == 320, "maximum size")
    try check(Preferences.normalizedSize(-1) == 80, "minimum size")
    try check(Motion.approach(10, target: 12, speed: 42, dt: 1) == 12, "no motion overshoot")
    try check(Motion.clamp(100, min: 20, max: 10) == 20, "small monitor bounds")
    do {
        _ = try safeResource("../Info.plist", root: root)
        throw ResourceError.invalid("TEST FAILED: path traversal accepted")
    } catch ResourceError.invalid(let reason) {
        if reason.hasPrefix("TEST FAILED") { throw ResourceError.invalid(reason) }
    }
    let temp = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
    defer { try? FileManager.default.removeItem(at: temp) }
    let settings = temp.appendingPathComponent("settings.json")
    var value = Preferences(); value.size = 176; value.choices = ["focus": "base_14"]
    try value.save(to: settings)
    try check(Preferences.load(from: settings).size == 176, "settings round trip")
    try check(Preferences.load(from: settings).choices["focus"] == "base_14", "image selection persistence")
    try Data("broken json".utf8).write(to: settings)
    try check(Preferences.load(from: settings).size == 144, "corrupt settings recovery")
    let app = try Desktop(root: root, settingsURL: settings)
    try check(app.defaults.count == 8, "all activity animations")
    try check(app.library.count == 47, "47 gallery animations")
    var frameCount = 0
    for item in app.library {
        let clip = try Clip(files: item.files, durations: item.durations_ms, root: root, normalize: true)
        for image in clip.frames {
            try check(image.cgImage(forProposedRect: nil, context: nil, hints: nil) != nil, "decode gallery frame")
        }
        frameCount += clip.frames.count
    }
    // Verify ImageIO import on bundled static and animated images without any personal files.
    let importedPNG = try Clip(imported: safeResource(app.library[0].Preview, root: root))
    try check(importedPNG.frames.count == 1, "static PNG import")
    let importedGIF = try Clip(imported: root.appendingPathComponent("walk_preview.gif"))
    try check(importedGIF.frames.count > 1, "animated GIF import")
    print("PASS: timer, pause/resume, playback, bounds, settings, import, all 8 activities, 47 library items (\(frameCount) frames)")
}

extension Desktop {
    func runSmokeTest() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [self] in
            do {
                try check(panel.isVisible, "pet window is visible")
                try check(panel.backgroundColor == .clear && !panel.isOpaque, "transparent panel")
                switchActivity(.focus)
                try check(session.active && activity == .focus, "start focus")
                pauseSession(); let held = session.elapsed(at: now)
                try check(session.paused && session.elapsed(at: now + 20) == held, "pause in app")
                pauseSession(); try check(!session.paused, "resume in app")
                for mode in [Activity.rest, .meeting, .offWork] { switchActivity(mode); tick() }
                resize(80); resize(320); resize(144)
                toggleVisibility(); try check(!panel.isVisible, "hide")
                toggleVisibility(); try check(panel.isVisible, "show")
                preferences.choices[Activity.offWork.rawValue] = "base_15"
                farewellUntil = 0; nextWalk = now + 10; tick()
                try check(pet.image != nil, "selected library image renders")
                let temporaryMenu = NSMenuItem(); temporaryMenu.representedObject = Activity.focus.rawValue
                openPicker(temporaryMenu)
                try check(picker?.isVisible == true, "modeless image picker")
                picker?.close()
                let screenshot = root.deletingLastPathComponent().deletingLastPathComponent()
                    .deletingLastPathComponent().appendingPathComponent("TwinkleAgent_macOS_preview.png")
                pet.displayIfNeeded()
                let bitmap = pet.bitmapImageRepForCachingDisplay(in: pet.bounds)!
                pet.cacheDisplay(in: pet.bounds, to: bitmap)
                try bitmap.representation(using: .png, properties: [:])!.write(to: screenshot)
                print("PASS: native AppKit window, activity changes, pause/resume, resize, hide/show, gallery selection, picker, rendered PNG")
                NSApplication.shared.terminate(nil)
            } catch {
                fputs("\(error)\n", stderr); exit(1)
            }
        }
    }
}
