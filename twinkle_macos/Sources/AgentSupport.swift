import AppKit
import Security

struct AgentFailure: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}

enum SecretStore {
    static let service = "io.github.seanko29.twinkle-agent.connections"
    static func read(_ name: String, service: String = service) throws -> Data? {
        let query: [CFString: Any] = [kSecClass: kSecClassGenericPassword,
            kSecAttrService: service, kSecAttrAccount: name,
            kSecReturnData: true, kSecMatchLimit: kSecMatchLimitOne]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw AgentFailure("Keychain access failed (\(status)).") }
        return result as? Data
    }
    static func write(_ data: Data, name: String, service: String = service) throws {
        let query: [CFString: Any] = [kSecClass: kSecClassGenericPassword,
            kSecAttrService: service, kSecAttrAccount: name]
        let status = SecItemUpdate(query as CFDictionary, [kSecValueData: data] as CFDictionary)
        if status == errSecItemNotFound {
            var insert = query
            insert[kSecValueData] = data
            insert[kSecAttrAccessible] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            guard SecItemAdd(insert as CFDictionary, nil) == errSecSuccess else {
                throw AgentFailure("Could not save connection in Keychain.")
            }
        } else if status != errSecSuccess { throw AgentFailure("Could not update Keychain connection.") }
    }
    static func remove(_ name: String, service: String = service) throws {
        let status = SecItemDelete([kSecClass: kSecClassGenericPassword,
            kSecAttrService: service, kSecAttrAccount: name] as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw AgentFailure("Could not remove Keychain connection.")
        }
    }
}

protocol HTTPSending {
    func send(_ request: URLRequest) async throws -> (Data, Int)
}

final class AgentHTTP: NSObject, HTTPSending, URLSessionTaskDelegate {
    lazy var session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.httpShouldSetCookies = false; config.urlCache = nil
        config.timeoutIntervalForRequest = 90; config.timeoutIntervalForResource = 180
        return URLSession(configuration: config, delegate: self, delegateQueue: nil)
    }()
    func send(_ request: URLRequest) async throws -> (Data, Int) {
        guard let url = request.url, url.scheme == "https",
              ["auth.openai.com", "api.openai.com"].contains(url.host ?? "") else {
            throw AgentFailure("Unsupported authentication or AI endpoint.")
        }
        let (data, response) = try await session.data(for: request)
        guard data.count <= 4 * 1024 * 1024, let response = response as? HTTPURLResponse else {
            throw AgentFailure("AI response exceeded the supported size.")
        }
        return (data, response.statusCode)
    }
    func urlSession(_ session: URLSession, task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
        completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
}

func formEncoded(_ values: [String: String]) -> Data {
    let allowed = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
    return Data(values.sorted { $0.key < $1.key }.map {
        $0.key.addingPercentEncoding(withAllowedCharacters: allowed)! + "="
            + $0.value.addingPercentEncoding(withAllowedCharacters: allowed)!
    }.joined(separator: "&").utf8)
}

struct ProcessOutput { let data: Data; let status: Int32 }

final class ChildProcess {
    private let lock = NSLock()
    private var active: Process?
    private var cancelled = false

    func cancel() {
        lock.lock(); cancelled = true; let process = active; lock.unlock()
        if process?.isRunning == true { process?.terminate() }
    }

    func run(executable: URL, arguments: [String], input: Data = Data(),
             directory: URL? = nil, timeout: Double = 120) async throws -> ProcessOutput {
        try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async { [self] in
                do {
                    let process = Process(), output = Pipe(), errors = Pipe(), stdin = Pipe()
                    process.executableURL = executable; process.arguments = arguments
                    process.currentDirectoryURL = directory
                    process.standardOutput = output; process.standardError = errors; process.standardInput = stdin
                    var environment = ProcessInfo.processInfo.environment
                    environment["PATH"] = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin",
                        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".local/bin").path,
                        environment["PATH"] ?? ""].joined(separator: ":")
                    process.environment = environment
                    lock.lock()
                    guard !cancelled else { lock.unlock(); throw CancellationError() }
                    active = process
                    do { try process.run(); lock.unlock() }
                    catch { active = nil; lock.unlock(); throw error }
                    // Drain both pipes concurrently so a verbose CLI cannot deadlock.
                    let group = DispatchGroup(), dataLock = NSLock()
                    var stdout = Data(), exceeded = false
                    group.enter()
                    DispatchQueue.global().async {
                        while true {
                            let chunk = output.fileHandleForReading.availableData
                            if chunk.isEmpty { break }
                            dataLock.lock()
                            if stdout.count + chunk.count <= 4 * 1024 * 1024 { stdout.append(chunk) }
                            else { exceeded = true; if process.isRunning { process.terminate() } }
                            dataLock.unlock()
                        }
                        group.leave()
                    }
                    group.enter()
                    DispatchQueue.global().async {
                        // Never log stderr: it can contain account details or prompt text.
                        while !errors.fileHandleForReading.availableData.isEmpty { }
                        group.leave()
                    }
                    let timeoutWork = DispatchWorkItem { if process.isRunning { process.terminate() } }
                    DispatchQueue.global().asyncAfter(deadline: .now() + timeout, execute: timeoutWork)
                    try stdin.fileHandleForWriting.write(contentsOf: input)
                    try stdin.fileHandleForWriting.close()
                    process.waitUntilExit(); timeoutWork.cancel(); group.wait()
                    lock.lock(); active = nil; let wasCancelled = cancelled; lock.unlock()
                    if wasCancelled { throw CancellationError() }
                    guard !exceeded else { throw AgentFailure("CLI output exceeded the supported size.") }
                    continuation.resume(returning: ProcessOutput(data: stdout, status: process.terminationStatus))
                } catch {
                    lock.lock(); let running = active; active = nil; lock.unlock()
                    if running?.isRunning == true { running?.terminate() }
                    continuation.resume(throwing: error)
                }
            }
        }
    }
}

enum ClaudeCode {
    static func executable() -> URL? {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let paths = [home.appendingPathComponent(".local/bin/claude").path,
                     "/opt/homebrew/bin/claude", "/usr/local/bin/claude"]
        return paths.first(where: { FileManager.default.isExecutableFile(atPath: $0) }).map { URL(fileURLWithPath: $0) }
    }
    static let summaryArguments = ["-p", "--safe-mode", "--output-format", "json", "--tools", "",
        "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--disable-slash-commands",
        "--no-chrome", "--no-session-persistence"]

    static func usesSubscription(_ auth: [String: Any]) -> Bool {
        ["claude.ai", "oauth_token"].contains(auth["authMethod"] as? String ?? "")
    }

    static func summarize(_ prompt: String, runner: ChildProcess, allowAPIBilling: Bool = false) async throws -> String {
        guard let executable = executable() else {
            throw AgentFailure("Claude Code를 설치한 뒤 로그인해 주세요. Install Claude Code and sign in first.")
        }
        let status = try await runner.run(executable: executable, arguments: ["--safe-mode", "auth", "status"], timeout: 20)
        guard status.status == 0,
              let auth = try? JSONSerialization.jsonObject(with: status.data) as? [String: Any] else {
            throw AgentFailure("Claude Code에서 먼저 로그인해 주세요. Sign in to Claude Code first.")
        }
        guard allowAPIBilling || usesSubscription(auth) else {
            throw AgentFailure("현재 Claude Code는 구독 인증이 아닙니다. 구독 계정으로 로그인하거나 AI 선택에서 API/클라우드 과금 모드를 직접 고르세요. Select subscription login or explicitly choose API/cloud billing mode.")
        }
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("twinkle-ai-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true,
                                                attributes: [.posixPermissions: 0o700])
        defer { try? FileManager.default.removeItem(at: folder) }
        let result = try await runner.run(executable: executable, arguments: summaryArguments,
                                         input: Data(prompt.utf8), directory: folder, timeout: 180)
        guard result.status == 0, let json = try? JSONSerialization.jsonObject(with: result.data) as? [String: Any],
              json["is_error"] as? Bool != true, let answer = json["result"] as? String, !answer.isEmpty else {
            throw AgentFailure("Claude Code 연결·사용량·로그인을 확인해 주세요. Check Claude Code login and usage limits.")
        }
        return answer
    }
}
