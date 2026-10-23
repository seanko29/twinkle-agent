import AppKit
import CryptoKit
import Network
import Security

func base64url(_ data: Data) -> String {
    data.base64EncodedString().replacingOccurrences(of: "+", with: "-")
        .replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "")
}
func decodeBase64url(_ value: String) -> Data? {
    var text = value.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
    text += String(repeating: "=", count: (4 - text.count % 4) % 4)
    return Data(base64Encoded: text)
}
func randomOAuthValue() throws -> String {
    var bytes = [UInt8](repeating: 0, count: 32)
    guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess else {
        throw AgentFailure("Unable to prepare secure sign-in.")
    }
    return base64url(Data(bytes))
}

struct ChatGPTAccount: Codable {
    let clientID: String
    let subject: String
    let email: String
    var idToken: String
    var accessToken: String
    var refreshToken: String
    var expiresAt: Date
    var scopes: [String]
}

enum IDTokenValidator {
    static func der(_ tag: UInt8, _ content: Data) -> Data {
        var length = Data()
        if content.count < 128 { length.append(UInt8(content.count)) }
        else {
            var bytes = [UInt8](), count = content.count
            while count > 0 { bytes.insert(UInt8(count & 255), at: 0); count >>= 8 }
            length.append(0x80 | UInt8(bytes.count)); length.append(contentsOf: bytes)
        }
        return Data([tag]) + length + content
    }
    static func integer(_ bytes: Data) -> Data {
        var value = bytes
        while value.count > 1 && value.first == 0 { value.removeFirst() }
        if (value.first ?? 0) >= 128 { value.insert(0, at: 0) }
        return der(0x02, value)
    }
    static func validate(_ jwt: String, clientID: String, nonce: String?,
                         jwks: [String: Any], now: Date = Date()) throws -> [String: Any] {
        let pieces = jwt.split(separator: ".", omittingEmptySubsequences: false).map(String.init)
        guard pieces.count == 3, let headerData = decodeBase64url(pieces[0]),
              let payload = decodeBase64url(pieces[1]), let signature = decodeBase64url(pieces[2]),
              let header = try JSONSerialization.jsonObject(with: headerData) as? [String: Any],
              let claims = try JSONSerialization.jsonObject(with: payload) as? [String: Any],
              header["alg"] as? String == "RS256", let kid = header["kid"] as? String,
              let keys = jwks["keys"] as? [[String: Any]],
              let jwk = keys.first(where: { $0["kid"] as? String == kid && $0["kty"] as? String == "RSA" }),
              let n = jwk["n"] as? String, let e = jwk["e"] as? String,
              let modulus = decodeBase64url(n), let exponent = decodeBase64url(e), modulus.count >= 256 else {
            throw AgentFailure("ChatGPT ID token signature format is invalid.")
        }
        let encoded = der(0x30, integer(modulus) + integer(exponent))
        let attributes: [CFString: Any] = [kSecAttrKeyType: kSecAttrKeyTypeRSA,
            kSecAttrKeyClass: kSecAttrKeyClassPublic, kSecAttrKeySizeInBits: modulus.count * 8]
        guard let key = SecKeyCreateWithData(encoded as CFData, attributes as CFDictionary, nil),
              SecKeyVerifySignature(key, .rsaSignatureMessagePKCS1v15SHA256,
                Data((pieces[0] + "." + pieces[1]).utf8) as CFData, signature as CFData, nil) else {
            throw AgentFailure("ChatGPT ID token signature could not be verified.")
        }
        let audiences = (claims["aud"] as? [String]) ?? [claims["aud"] as? String ?? ""]
        guard claims["iss"] as? String == "https://auth.openai.com", audiences.contains(clientID),
              let expiry = claims["exp"] as? Double, expiry > now.timeIntervalSince1970,
              (claims["nbf"] as? Double ?? 0) <= now.timeIntervalSince1970 + 30,
              (nonce == nil || claims["nonce"] as? String == nonce),
              let subject = claims["sub"] as? String, !subject.isEmpty else {
            throw AgentFailure("ChatGPT account identity or sign-in attempt did not match.")
        }
        if audiences.count > 1 && claims["azp"] as? String != clientID {
            throw AgentFailure("ChatGPT ID token authorized party did not match.")
        }
        return claims
    }
}

final class OAuthCallback {
    private var listener: NWListener?
    private var pending: CheckedContinuation<URL, Error>?
    private var result: Result<URL, Error>?
    private let queue = DispatchQueue(label: "Twinkle.OAuth.callback")
    private var readyContinuation: CheckedContinuation<UInt16, Error>?
    private var connections: [NWConnection] = []

    func start() async throws -> UInt16 {
        try await withCheckedThrowingContinuation { continuation in
            queue.async { [self] in
                do {
                    let parameters = NWParameters.tcp
                    parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: .any)
                    let server = try NWListener(using: parameters)
                    readyContinuation = continuation; listener = server
                    server.stateUpdateHandler = { [weak self] state in
                        guard let self = self else { return }
                        switch state {
                        case .ready:
                            if let port = self.listener?.port?.rawValue { self.readyContinuation?.resume(returning: port) }
                            else { self.readyContinuation?.resume(throwing: AgentFailure("Sign-in callback has no port.")) }
                            self.readyContinuation = nil
                        case .failed(let error):
                            self.readyContinuation?.resume(throwing: error); self.readyContinuation = nil
                            self.finish(.failure(error))
                        default: break
                        }
                    }
                    server.newConnectionHandler = { [weak self] connection in
                        guard let self = self else { connection.cancel(); return }
                        self.connections.append(connection); connection.start(queue: self.queue)
                        self.read(connection, data: Data())
                        self.queue.asyncAfter(deadline: .now() + 10) { connection.cancel() }
                    }
                    server.start(queue: queue)
                    queue.asyncAfter(deadline: .now() + 300) { [weak self] in
                        self?.finish(.failure(AgentFailure("로그인 시간이 만료됐어요. Sign-in timed out.")))
                    }
                } catch { continuation.resume(throwing: error) }
            }
        }
    }

    func wait() async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            queue.async { [self] in
                if let result = result { continuation.resume(with: result) }
                else { pending = continuation }
            }
        }
    }

    private func read(_ connection: NWConnection, data: Data) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 8192) { [weak self] chunk, _, complete, error in
            guard let self = self else { connection.cancel(); return }
            var buffer = data; buffer.append(chunk ?? Data())
            guard buffer.count <= 16384, error == nil else { connection.cancel(); return }
            guard let request = String(data: buffer, encoding: .utf8), request.contains("\r\n\r\n") else {
                if !complete { self.read(connection, data: buffer) } else { connection.cancel() }
                return
            }
            let parts = request.components(separatedBy: "\r\n")[0].split(separator: " ")
            guard parts.count == 3, parts[0] == "GET", parts[1].hasPrefix("/auth/callback?"),
                  let url = URL(string: "http://127.0.0.1" + parts[1]), url.path == "/auth/callback" else {
                connection.cancel(); return
            }
            let body = "<!doctype html><meta charset=utf-8><title>Twinkle Agent</title><p>Return to Twinkle Agent to finish sign-in. 트윙클로 돌아가 연결 결과를 확인해 주세요.</p>"
            let response = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: \(body.utf8.count)\r\nConnection: close\r\n\r\n" + body
            connection.send(content: Data(response.utf8), completion: .contentProcessed { _ in connection.cancel() })
            self.finish(.success(url))
        }
    }

    private func finish(_ value: Result<URL, Error>) {
        guard result == nil else { return }
        result = value; pending?.resume(with: value); pending = nil
        listener?.cancel(); listener = nil
    }
    func cancel() {
        queue.async { [self] in
            readyContinuation?.resume(throwing: CancellationError()); readyContinuation = nil
            finish(.failure(CancellationError())); connections.forEach { $0.cancel() }; connections = []
        }
    }
}

final class ChatGPTConnection {
    let http: HTTPSending
    private(set) var account: ChatGPTAccount?
    private var callback: OAuthCallback?
    private let hostID: String
    private let credentialName = "chatgpt-account"

    init(http: HTTPSending = AgentHTTP(), loadSaved: Bool = true, fixtureAccount: ChatGPTAccount? = nil) {
        self.http = http
        let key = "TwinkleAgent.ChatGPT.hostID"
        if let stored = UserDefaults.standard.string(forKey: key) { hostID = stored }
        else {
            let value = "urn:uuid:" + UUID().uuidString.lowercased(); hostID = value
            if loadSaved { UserDefaults.standard.set(value, forKey: key) }
        }
        if loadSaved, let data = try? SecretStore.read(credentialName) {
            account = try? JSONDecoder().decode(ChatGPTAccount.self, from: data)
        }
        if !loadSaved { account = fixtureAccount }
    }
    func cancelLogin() { callback?.cancel(); callback = nil }
    func disconnect() async throws -> Bool {
        cancelLogin()
        var revoked = account == nil
        if let stored = account {
            do {
                var discovery = URLRequest(url: URL(string: "https://auth.openai.com/.well-known/openid-configuration")!)
                discovery.timeoutInterval = 15
                let (data, status) = try await http.send(discovery)
                guard status == 200, let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                      let value = json["revocation_endpoint"] as? String,
                      let endpoint = URL(string: value), endpoint.scheme == "https", endpoint.host == "auth.openai.com" else {
                    throw AgentFailure("No trusted revocation endpoint.")
                }
                var request = URLRequest(url: endpoint); request.httpMethod = "POST"; request.timeoutInterval = 15
                request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
                request.httpBody = formEncoded(["token": stored.refreshToken, "token_type_hint": "refresh_token", "client_id": stored.clientID])
                for attempt in 0..<2 {
                    let (_, code) = try await http.send(request)
                    if code == 200 { revoked = true; break }
                    if code < 500 { break }
                    if attempt == 0 { try await Task.sleep(nanoseconds: 1_000_000_000) }
                }
            } catch { revoked = false }
        }
        try SecretStore.remove(credentialName); account = nil
        return revoked
    }

    static func callbackValues(_ response: URL, state: String, previousClient: String?) throws -> [String: String] {
        let items = URLComponents(url: response, resolvingAgainstBaseURL: false)?.queryItems ?? []
        guard Set(items.map(\.name)).count == items.count else { throw AgentFailure("Duplicate OAuth callback parameters.") }
        let values = Dictionary(uniqueKeysWithValues: items.map { ($0.name, $0.value ?? "") })
        guard values["state"] == state else { throw AgentFailure("Sign-in state did not match. Try again.") }
        guard values["error"] == nil else { throw AgentFailure("ChatGPT 로그인 또는 구독 사용 승인이 취소됐어요. Sign-in or plan access was declined.") }
        let client = values["client_id"] ?? previousClient ?? ""
        guard !client.isEmpty, client != "dynamic_agent_client", previousClient == nil || client == previousClient,
              let code = values["code"], !code.isEmpty else { throw AgentFailure("Incomplete ChatGPT client registration.") }
        var checked = values; checked["client_id"] = client; return checked
    }

    func signIn() async throws {
        guard callback == nil else { throw AgentFailure("A sign-in is already in progress.") }
        let listener = OAuthCallback(); callback = listener
        defer { listener.cancel(); callback = nil }
        let state = try randomOAuthValue(), nonce = try randomOAuthValue(), verifier = try randomOAuthValue()
        let port = try await listener.start()
        let redirect = "http://127.0.0.1:\(port)/auth/callback"
        let previous = account
        let client = previous?.clientID ?? "dynamic_agent_client"
        var parameters = ["client_id": client, "ext_agent_host_id": hostID,
            "response_type": "code", "redirect_uri": redirect,
            "scope": "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
            "resource": "https://api.openai.com/v1", "state": state, "nonce": nonce,
            "code_challenge_method": "S256", "code_challenge": base64url(Data(SHA256.hash(data: Data(verifier.utf8))))]
        if let previous = previous { parameters["id_token_hint"] = previous.idToken }
        else { parameters["agent_name_hint"] = "Twinkle Agent" }
        var url = URLComponents(string: "https://auth.openai.com/api/accounts/authorize")!
        url.queryItems = parameters.map { URLQueryItem(name: $0.key, value: $0.value) }
        let authorizationURL = url.url!
        let opened = await MainActor.run { NSWorkspace.shared.open(authorizationURL) }
        guard opened else { throw AgentFailure("Could not open the sign-in browser.") }
        let response = try await listener.wait()
        let values = try Self.callbackValues(response, state: state, previousClient: previous?.clientID)
        let issuedClient = values["client_id"]!, code = values["code"]!
        let token = try await tokenRequest(["grant_type": "authorization_code", "client_id": issuedClient,
            "code": code, "code_verifier": verifier, "redirect_uri": redirect, "resource": "https://api.openai.com/v1"])
        guard let idToken = token["id_token"] as? String else { throw AgentFailure("Missing ChatGPT ID token.") }
        var request = URLRequest(url: URL(string: "https://auth.openai.com/.well-known/jwks.json")!)
        request.httpMethod = "GET"
        let (data, status) = try await http.send(request)
        guard status == 200, let keys = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw AgentFailure("Could not load ChatGPT account verification keys.")
        }
        let claims = try IDTokenValidator.validate(idToken, clientID: issuedClient, nonce: nonce, jwks: keys)
        if let previous = previous, claims["sub"] as? String != previous.subject {
            throw AgentFailure("Different ChatGPT account. Disconnect the old account first.")
        }
        let connected = try Self.makeAccount(token: token, clientID: issuedClient,
            subject: claims["sub"] as! String, email: claims["email"] as? String ?? "ChatGPT", previous: nil)
        try SecretStore.write(JSONEncoder().encode(connected), name: credentialName)
        account = connected
    }

    static func makeAccount(token: [String: Any], clientID: String, subject: String,
                            email: String, previous: ChatGPTAccount?) throws -> ChatGPTAccount {
        let scopes = (token["scope"] as? String)?.split(separator: " ").map(String.init) ?? previous?.scopes ?? []
        guard scopes.contains("chatgpt.tokens.use.direct"), scopes.contains("resource.invoke"),
              let access = token["access_token"] as? String, !access.isEmpty,
              (token["token_type"] as? String)?.lowercased() == "bearer",
              let expiry = token["expires_in"] as? Double, expiry > 0,
              let refresh = token["refresh_token"] as? String ?? previous?.refreshToken, !refresh.isEmpty,
              let idToken = token["id_token"] as? String ?? previous?.idToken else {
            throw AgentFailure("ChatGPT 구독 사용 권한이 필요해요. Authorize ChatGPT plan usage, not sign-in alone.")
        }
        return ChatGPTAccount(clientID: clientID, subject: subject, email: email, idToken: idToken,
            accessToken: access, refreshToken: refresh, expiresAt: Date().addingTimeInterval(expiry), scopes: scopes)
    }

    private func tokenRequest(_ values: [String: String]) async throws -> [String: Any] {
        var request = URLRequest(url: URL(string: "https://auth.openai.com/api/accounts/oauth/token")!)
        request.httpMethod = "POST"; request.httpBody = formEncoded(values)
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        let (data, status) = try await http.send(request)
        guard status == 200, let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw AgentFailure("ChatGPT 인증이 만료되었거나 거부됐어요. Reconnect your ChatGPT account.")
        }
        return json
    }

    func accessToken() async throws -> String {
        guard let stored = account else { throw AgentFailure("먼저 Continue with ChatGPT로 연결해 주세요. Connect ChatGPT first.") }
        if stored.expiresAt > Date().addingTimeInterval(60) { return stored.accessToken }
        let token = try await tokenRequest(["grant_type": "refresh_token", "client_id": stored.clientID,
            "refresh_token": stored.refreshToken, "resource": "https://api.openai.com/v1"])
        if let newIDToken = token["id_token"] as? String {
            let (data, status) = try await http.send(URLRequest(url: URL(string: "https://auth.openai.com/.well-known/jwks.json")!))
            guard status == 200, let keys = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  try IDTokenValidator.validate(newIDToken, clientID: stored.clientID, nonce: nil, jwks: keys)["sub"] as? String == stored.subject else {
                throw AgentFailure("Refreshed ChatGPT identity did not match.")
            }
        }
        let refreshed = try Self.makeAccount(token: token, clientID: stored.clientID,
            subject: stored.subject, email: stored.email, previous: stored)
        try SecretStore.write(JSONEncoder().encode(refreshed), name: credentialName)
        account = refreshed
        return refreshed.accessToken
    }

    func models() async throws -> [(id: String, name: String)] {
        var request = URLRequest(url: URL(string: "https://api.openai.com/v1/models")!)
        request.setValue("Bearer " + (try await accessToken()), forHTTPHeaderField: "Authorization")
        let (data, status) = try await http.send(request)
        guard status == 200, let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let models = json["models"] as? [[String: Any]] else {
            throw AgentFailure("ChatGPT 모델을 불러오지 못했어요. Check account plan access and try again.")
        }
        let visible = models.compactMap { item -> (String, String)? in
            guard item["visibility"] as? String == "list", let id = item["slug"] as? String else { return nil }
            return (id, item["display_name"] as? String ?? id)
        }
        guard !visible.isEmpty else { throw AgentFailure("No eligible models are available for this ChatGPT account.") }
        return visible
    }

    func summarize(_ prompt: String, model: String) async throws -> String {
        guard !model.isEmpty else { throw AgentFailure("Choose an available ChatGPT model first.") }
        var request = URLRequest(url: URL(string: "https://api.openai.com/v1/responses")!)
        request.httpMethod = "POST"
        request.setValue("Bearer " + (try await accessToken()), forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("text/event-stream", forHTTPHeaderField: "Accept")
        request.httpBody = try JSONSerialization.data(withJSONObject: ["model": model,
            "input": [["role": "user", "content": prompt]], "store": false, "stream": true])
        let (data, status) = try await http.send(request)
        guard status == 200 else {
            throw AgentFailure("ChatGPT 요청 실패 (HTTP \(status)). 구독 사용량·권한·로그인을 확인해 주세요.")
        }
        return try Self.readStream(data)
    }

    static func readStream(_ data: Data) throws -> String {
        guard let stream = String(data: data, encoding: .utf8) else { throw AgentFailure("Invalid AI stream encoding.") }
        var text = "", completed = false
        for line in stream.components(separatedBy: .newlines) where line.hasPrefix("data:") {
            let value = String(line.dropFirst(5)).trimmingCharacters(in: .whitespaces)
            if value == "[DONE]" { continue }
            guard let encoded = value.data(using: .utf8),
                  let event = try JSONSerialization.jsonObject(with: encoded) as? [String: Any],
                  let type = event["type"] as? String else { continue }
            if type == "response.output_text.delta" { text += event["delta"] as? String ?? "" }
            if ["response.failed", "response.incomplete", "error"].contains(type) {
                throw AgentFailure("ChatGPT 요약이 완료되지 않았어요. 사용량 한도 또는 연결 상태를 확인해 주세요. The response did not complete.")
            }
            if type == "response.completed" { completed = true }
        }
        guard completed, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw AgentFailure("ChatGPT 응답이 중단됐어요. Interrupted response; try again.")
        }
        return text
    }
}
