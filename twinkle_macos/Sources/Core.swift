import Foundation

enum Activity: String, CaseIterable, Codable {
    case focus, rest, meeting, offWork

    var animation: String { self == .offWork ? "idle" : rawValue }
    func title(_ english: Bool) -> String {
        switch self {
        case .focus: return english ? "Focus" : "집중"
        case .rest: return english ? "Rest" : "휴식"
        case .meeting: return english ? "Meeting" : "회의"
        case .offWork: return english ? "Off work" : "퇴근"
        }
    }
}

struct Session {
    private(set) var active = false
    private(set) var paused = false
    private var accumulated: Double = 0
    private var started: Double = 0

    mutating func start(at now: Double) {
        active = true; paused = false; accumulated = 0; started = now
    }
    func elapsed(at now: Double) -> Double {
        accumulated + (active && !paused ? max(0, now - started) : 0)
    }
    mutating func togglePause(at now: Double) {
        guard active else { return }
        if paused { started = now } else { accumulated = elapsed(at: now) }
        paused.toggle()
    }
    mutating func stop() { self = Session() }
    static func text(_ seconds: Double) -> String {
        let value = Int(max(0, seconds).rounded(.down))
        return value >= 3600
            ? String(format: "%02d:%02d:%02d", value / 3600, value / 60 % 60, value % 60)
            : String(format: "%02d:%02d", value / 60, value % 60)
    }
}

struct AnimationSpec: Decodable {
    let files: [String]
    let durations_ms: [Double]
}

struct LibraryItem: Decodable {
    let Id: String
    let Title: String
    let Group: String
    let Preview: String
    let files: [String]
    let durations_ms: [Double]
}

enum Playback {
    static func frame(at age: Double, durations: [Double]) -> Int {
        guard !durations.isEmpty else { return 0 }
        let safe = durations.map { $0.isFinite ? max(20, $0) : 100 }
        var offset = max(0, age).truncatingRemainder(dividingBy: safe.reduce(0, +))
        for (index, duration) in safe.enumerated() {
            if offset < duration { return index }
            offset -= duration
        }
        return safe.count - 1
    }
}

struct Preferences: Codable {
    var size: Double = 144
    var roaming = true
    var english = Locale.preferredLanguages.first?.hasPrefix("en") ?? false
    var choices: [String: String] = [:]

    static func normalizedSize(_ value: Double) -> Double {
        value.isFinite ? min(320, max(80, value)).rounded() : 144
    }

    static var directory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("TwinkleAgent", isDirectory: true)
    }

    static func load(from url: URL) -> Preferences {
        guard let data = try? Data(contentsOf: url),
              var value = try? JSONDecoder().decode(Preferences.self, from: data) else {
            return Preferences()
        }
        value.size = normalizedSize(value.size)
        // Only known bundled IDs or generated import names can be resolved.
        value.choices = value.choices.filter { key, value in
            Activity(rawValue: key) != nil && !value.contains("/") && !value.contains("\\")
        }
        return value
    }

    func save(to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try JSONEncoder().encode(self).write(to: url, options: .atomic)
    }
}

enum ResourceError: Error, CustomStringConvertible {
    case invalid(String)
    var description: String {
        switch self { case .invalid(let text): return text }
    }
}

func safeResource(_ relative: String, root: URL) throws -> URL {
    let normalizedRoot = root.standardizedFileURL.resolvingSymlinksInPath()
    let url = root.appendingPathComponent(relative).standardizedFileURL.resolvingSymlinksInPath()
    guard !relative.hasPrefix("/"), url.path.hasPrefix(normalizedRoot.path + "/"),
          FileManager.default.fileExists(atPath: url.path) else {
        throw ResourceError.invalid("Missing or invalid resource: \(relative)")
    }
    return url
}

enum Motion {
    static func clamp(_ value: Double, min lower: Double, max upper: Double) -> Double {
        Swift.max(lower, Swift.min(Swift.max(lower, upper), value))
    }
    static func approach(_ current: Double, target: Double, speed: Double, dt: Double) -> Double {
        let step = max(0, speed * dt)
        return current < target ? min(target, current + step) : max(target, current - step)
    }
}
