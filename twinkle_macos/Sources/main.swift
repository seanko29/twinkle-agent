import AppKit

let application = NSApplication.shared
application.setActivationPolicy(.accessory)
guard let resourceRoot = Bundle.main.resourceURL else {
    fputs("Missing application bundle resources\n", stderr); exit(1)
}

do {
    if CommandLine.arguments.contains("--self-test") {
        try runSelfTests(root: resourceRoot)
    } else {
        let smoke = CommandLine.arguments.contains("--smoke-test")
        let testSettings = smoke ? FileManager.default.temporaryDirectory
            .appendingPathComponent("twinkle-smoke-" + UUID().uuidString).appendingPathComponent("settings.json") : nil
        let desktop = try Desktop(root: resourceRoot, settingsURL: testSettings)
        desktop.smokeTest = smoke
        application.delegate = desktop
        withExtendedLifetime(desktop) { application.run() }
        if let folder = testSettings?.deletingLastPathComponent() { try? FileManager.default.removeItem(at: folder) }
    }
} catch {
    if CommandLine.arguments.contains("--self-test") || CommandLine.arguments.contains("--smoke-test") {
        fputs("\(error)\n", stderr); exit(1)
    }
    let alert = NSAlert()
    alert.messageText = "Twinkle Agent could not start / 트윙클을 실행할 수 없어요"
    alert.informativeText = String(describing: error)
    alert.runModal(); exit(1)
}
