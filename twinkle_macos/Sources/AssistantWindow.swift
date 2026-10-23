import AppKit
import UserNotifications

struct AssistantSettings: Codable {
    var provider = "chatgpt"
    var model = ""
    var monitorMail = false
    var autoSummarize = false
    var seenIDs: [Int]? = nil
    static func load(_ url: URL) -> AssistantSettings {
        guard let data = try? Data(contentsOf: url),
              let value = try? JSONDecoder().decode(Self.self, from: data) else { return Self() }
        return value
    }
}

final class AssistantWindow: NSObject, NSTableViewDataSource, NSTableViewDelegate, UNUserNotificationCenterDelegate {
    weak var desktop: Desktop?
    let chatGPT: ChatGPTConnection
    let settingsURL: URL
    var settings: AssistantSettings
    var window: NSWindow?
    let table = NSTableView()
    let provider = NSPopUpButton()
    let model = NSPopUpButton()
    let accountStatus = NSTextField(wrappingLabelWithString: "")
    let activityStatus = NSTextField(wrappingLabelWithString: "")
    let consent = NSTextField(wrappingLabelWithString: "")
    let question = NSTextField(string: "")
    let output = NSTextView()
    let monitor = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    let automatic = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    var controls: [NSControl] = []
    var messages: [MailMessage] = []
    var task: Task<Void, Never>?
    var runner: ChildProcess?
    var pollTimer: Timer?
    var modelIDs: [String] = []
    var busy = false
    var unreadCount: Int { messages.count }
    var english: Bool { desktop?.preferences.english ?? false }
    func text(_ ko: String, _ en: String) -> String { english ? en : ko }

    init(desktop: Desktop) {
        self.desktop = desktop
        settingsURL = desktop.settingsURL.deletingLastPathComponent().appendingPathComponent("assistant.json")
        settings = desktop.smokeTest ? AssistantSettings() : AssistantSettings.load(settingsURL)
        chatGPT = ChatGPTConnection(loadSaved: !desktop.smokeTest)
        super.init()
        UNUserNotificationCenter.current().delegate = self
        if settings.monitorMail && !desktop.smokeTest { startPolling() }
    }

    func stop() { task?.cancel(); runner?.cancel(); chatGPT.cancelLogin(); pollTimer?.invalidate() }
    func save() {
        do {
            try FileManager.default.createDirectory(at: settingsURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            try JSONEncoder().encode(settings).write(to: settingsURL, options: .atomic)
        } catch { activityStatus.stringValue = text("설정을 저장하지 못했어요.", "Could not save settings.") }
    }

    func button(_ title: String, _ action: Selector, _ frame: NSRect, parent: NSView) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.frame = frame; parent.addSubview(button); controls.append(button); return button
    }
    func label(_ title: String, _ frame: NSRect, parent: NSView) {
        let label = NSTextField(labelWithString: title); label.frame = frame; parent.addSubview(label)
    }

    func show() {
        if window == nil { buildWindow() }
        updateAccountStatus()
        NSApplication.shared.activate(ignoringOtherApps: true); window?.makeKeyAndOrderFront(nil)
    }

    func buildWindow() {
        let opened = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 800, height: 760),
            styleMask: [.titled, .closable, .miniaturizable], backing: .buffered, defer: false)
        opened.title = text("트윙클 · 메일과 AI", "Twinkle · Mail & AI")
        opened.isReleasedWhenClosed = false
        let view = opened.contentView!
        label(text("함께 읽고, 요약하고, 다음 할 일을 찾아요.", "Read together, summarize, and find the next action."),
              NSRect(x: 22, y: 715, width: 755, height: 25), parent: view)
        provider.addItems(withTitles: [text("ChatGPT 구독", "ChatGPT plan"), "Claude Code (subscription / API)"])
        provider.selectItem(at: settings.provider == "claude" ? 1 : 0)
        provider.frame = NSRect(x: 20, y: 675, width: 245, height: 30)
        provider.target = self; provider.action = #selector(providerChanged); view.addSubview(provider); controls.append(provider)
        model.frame = NSRect(x: 275, y: 675, width: 290, height: 30)
        model.target = self; model.action = #selector(modelChanged); view.addSubview(model); controls.append(model)
        button(text("모델 새로고침", "Refresh models"), #selector(refreshModels), NSRect(x: 575, y: 675, width: 200, height: 30), parent: view)
        button("Continue with ChatGPT", #selector(connectChatGPT), NSRect(x: 20, y: 635, width: 230, height: 32), parent: view)
        button(text("Claude Code 로그인", "Claude Code sign-in"), #selector(connectClaude), NSRect(x: 255, y: 635, width: 230, height: 32), parent: view)
        button(text("연결 확인", "Check connection"), #selector(checkConnection), NSRect(x: 490, y: 635, width: 140, height: 32), parent: view)
        button(text("GPT 연결 해제", "Disconnect GPT"), #selector(disconnectGPT), NSRect(x: 635, y: 635, width: 140, height: 32), parent: view)
        accountStatus.frame = NSRect(x: 24, y: 597, width: 750, height: 32)
        accountStatus.font = .systemFont(ofSize: 11); accountStatus.textColor = .secondaryLabelColor; view.addSubview(accountStatus)
        button(text("Mail 앱 열기 / 계정 추가", "Open Mail / add account"), #selector(openMail), NSRect(x: 20, y: 550, width: 255, height: 32), parent: view)
        button(text("안 읽은 메일 가져오기", "Load unread mail"), #selector(loadMail), NSRect(x: 280, y: 550, width: 240, height: 32), parent: view)
        button(text("연결 도움말", "Connection help"), #selector(help), NSRect(x: 530, y: 550, width: 245, height: 32), parent: view)
        monitor.title = text("새 메일 확인 (1분 간격)", "Check new mail every minute")
        monitor.frame = NSRect(x: 24, y: 514, width: 300, height: 25)
        monitor.state = settings.monitorMail ? .on : .off
        monitor.target = self; monitor.action = #selector(monitorChanged); view.addSubview(monitor)
        automatic.title = text("새 메일 본문을 AI에 보내 자동 요약", "Send new mail to AI for automatic summaries")
        automatic.frame = NSRect(x: 325, y: 514, width: 450, height: 25)
        automatic.state = settings.autoSummarize ? .on : .off
        automatic.target = self; automatic.action = #selector(autoChanged); view.addSubview(automatic)
        let scroll = NSScrollView(frame: NSRect(x: 22, y: 300, width: 756, height: 204))
        scroll.hasVerticalScroller = true; scroll.borderType = .bezelBorder
        for (id, title, width) in [("subject", text("제목", "Subject"), 440.0), ("sender", text("보낸 사람", "Sender"), 290.0)] {
            let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(id))
            column.title = title; column.width = width; table.addTableColumn(column)
        }
        table.allowsMultipleSelection = true; table.dataSource = self; table.delegate = self; table.rowHeight = 26
        scroll.documentView = table; view.addSubview(scroll)
        consent.stringValue = text("선택한 1~5개 메일의 본문을 선택한 AI에 보냅니다. 메일을 읽음 처리하거나 보내지 않아요.",
            "Sends the bodies of 1–5 selected messages to your chosen AI. Does not mark read or send mail.")
        consent.frame = NSRect(x: 24, y: 265, width: 750, height: 30); consent.font = .systemFont(ofSize: 11)
        consent.textColor = .secondaryLabelColor; view.addSubview(consent)
        question.frame = NSRect(x: 23, y: 226, width: 510, height: 28)
        question.stringValue = text("핵심 내용과 내가 해야 할 일, 기한을 알려줘.", "Summarize the key points, my next actions, and deadlines.")
        view.addSubview(question); controls.append(question)
        button(text("선택 메일 요약", "Summarize selected"), #selector(summarizeSelected), NSRect(x: 540, y: 225, width: 235, height: 32), parent: view)
        let results = NSScrollView(frame: NSRect(x: 23, y: 61, width: 753, height: 151))
        results.hasVerticalScroller = true; results.borderType = .bezelBorder
        output.isEditable = false; output.isSelectable = true; output.isRichText = false
        output.font = .systemFont(ofSize: 13); output.textContainerInset = NSSize(width: 10, height: 10)
        output.isVerticallyResizable = true; output.isHorizontallyResizable = false
        output.autoresizingMask = [.width]; output.textContainer?.widthTracksTextView = true
        output.frame = NSRect(x: 0, y: 0, width: 750, height: 151)
        results.documentView = output; view.addSubview(results)
        activityStatus.frame = NSRect(x: 25, y: 17, width: 490, height: 32)
        activityStatus.font = .systemFont(ofSize: 11); view.addSubview(activityStatus)
        button(text("요약 복사", "Copy summary"), #selector(copySummary), NSRect(x: 535, y: 17, width: 125, height: 30), parent: view)
        let cancel = NSButton(title: text("작업 취소", "Cancel task"), target: self, action: #selector(cancelTask))
        cancel.frame = NSRect(x: 665, y: 17, width: 110, height: 30); view.addSubview(cancel)
        opened.center(); window = opened
        if model.numberOfItems == 0 { model.addItem(withTitle: text("연결 후 모델 선택", "Connect to choose a model")) }
        providerChanged()
        setBusy(busy)
    }

    func numberOfRows(in tableView: NSTableView) -> Int { messages.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard messages.indices.contains(row) else { return nil }
        let value = tableColumn?.identifier.rawValue == "sender" ? messages[row].sender : messages[row].subject
        let field = NSTextField(labelWithString: value.isEmpty ? "—" : value)
        field.lineBreakMode = .byTruncatingTail; field.toolTip = value; return field
    }

    func setBusy(_ value: Bool) {
        busy = value; controls.forEach { $0.isEnabled = !value }
        desktop?.assistantBusy = value
    }
    func launch(_ operation: @escaping @MainActor () async throws -> Void) {
        guard !busy else { return }
        setBusy(true)
        task = Task { @MainActor [weak self] in
            guard let self = self else { return }
            defer { self.setBusy(false); self.runner = nil; self.task = nil }
            do { try await operation() }
            catch is CancellationError { self.activityStatus.stringValue = self.text("취소했어요.", "Cancelled.") }
            catch { self.activityStatus.stringValue = error.localizedDescription }
        }
    }
    @objc func cancelTask() { task?.cancel(); runner?.cancel(); chatGPT.cancelLogin() }

    func updateAccountStatus() {
        let gpt = chatGPT.account.map { text("ChatGPT 연결됨 · ", "ChatGPT connected · ") + $0.email }
            ?? text("ChatGPT 미연결", "ChatGPT disconnected")
        let claude = ClaudeCode.executable() == nil ? text("Claude Code 설치 필요", "Claude Code not installed")
            : text("Claude Code 감지됨 · 연결 확인을 누르세요", "Claude Code found · check connection")
        accountStatus.stringValue = gpt + "  /  " + claude
    }

    @objc func providerChanged() {
        settings.provider = provider.indexOfSelectedItem == 1 ? "claude" : "chatgpt"
        model.isHidden = settings.provider == "claude"; save()
    }
    @objc func modelChanged() {
        if modelIDs.indices.contains(model.indexOfSelectedItem) { settings.model = modelIDs[model.indexOfSelectedItem]; save() }
    }
    @objc func connectChatGPT() {
        launch { [self] in
            activityStatus.stringValue = text("브라우저에서 로그인과 구독 사용을 승인해 주세요.", "Approve sign-in and plan usage in your browser.")
            try await chatGPT.signIn(); updateAccountStatus(); try await updateModels()
            activityStatus.stringValue = text("ChatGPT 구독 연결 완료", "ChatGPT plan connected")
        }
    }
    @objc func disconnectGPT() {
        guard !busy else { return }
        do {
            try chatGPT.disconnect(); modelIDs = []; model.removeAllItems(); settings.model = ""; save(); updateAccountStatus()
            activityStatus.stringValue = text("로컬 연결을 해제했어요. ChatGPT 설정에서도 앱 접근을 해제할 수 있어요.", "Local connection removed. You can revoke app access in ChatGPT settings.")
        } catch { activityStatus.stringValue = error.localizedDescription }
    }
    @MainActor func updateModels() async throws {
        let available = try await chatGPT.models()
        modelIDs = available.map(\.id); model.removeAllItems(); model.addItems(withTitles: available.map(\.name))
        let selected = modelIDs.firstIndex(of: settings.model) ?? 0
        model.selectItem(at: selected); settings.model = modelIDs[selected]; save()
    }
    @objc func refreshModels() {
        if provider.indexOfSelectedItem == 1 { checkConnection(); return }
        launch { [self] in try await updateModels(); activityStatus.stringValue = text("사용 가능한 모델을 불러왔어요.", "Available models loaded.") }
    }
    @objc func connectClaude() {
        guard let executable = ClaudeCode.executable() else {
            NSWorkspace.shared.open(URL(string: "https://code.claude.com/docs/en/quickstart")!)
            activityStatus.stringValue = text("공식 Claude Code를 설치한 뒤 다시 눌러 주세요.", "Install official Claude Code, then click sign-in again."); return
        }
        launch { [self] in
            activityStatus.stringValue = text("Claude Code의 브라우저 로그인 안내를 따라 주세요.", "Follow Claude Code's browser sign-in.")
            let process = ChildProcess(); runner = process
            let result = try await process.run(executable: executable, arguments: ["auth", "login"], timeout: 300)
            guard result.status == 0 else { throw AgentFailure("Claude Code 로그인을 완료하지 못했어요. Complete sign-in in Claude Code.") }
            try await checkClaudeStatus()
        }
    }
    @MainActor func checkClaudeStatus() async throws {
        guard let executable = ClaudeCode.executable() else { throw AgentFailure("Claude Code 설치가 필요해요. Install Claude Code first.") }
        let process = ChildProcess(); runner = process
        let response = try await process.run(executable: executable, arguments: ["auth", "status"], timeout: 20)
        guard response.status == 0,
              let json = try? JSONSerialization.jsonObject(with: response.data) as? [String: Any] else {
            throw AgentFailure("Claude Code에서 로그인해 주세요. Sign in to Claude Code first.")
        }
        let method = json["authMethod"] as? String ?? "unknown"
        accountStatus.stringValue = "Claude Code · " + method
            + text(" · 본인의 구독/크레딧/API 한도를 사용합니다.", " · uses your own plan, credits, or API billing.")
        activityStatus.stringValue = text("Claude Code 연결 확인 완료", "Claude Code connection checked")
    }
    @objc func checkConnection() {
        launch { [self] in
            if settings.provider == "claude" { try await checkClaudeStatus() }
            else { try await updateModels(); updateAccountStatus(); activityStatus.stringValue = text("ChatGPT 연결 확인 완료", "ChatGPT connection checked") }
        }
    }
    @objc func openMail() { NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/Mail.app")) }
    @objc func help() {
        let alert = NSAlert(); alert.messageText = text("연결하기", "Connect your services")
        alert.informativeText = text(
            "1. Mail → 설정 → 계정에서 Gmail·Outlook·네이버 등의 계정을 추가하고 동기화하세요.\n2. 안 읽은 메일 가져오기를 누르고 macOS의 Mail 자동화 접근을 허용하세요.\n3. Continue with ChatGPT에서 구독 사용을 승인하거나, 공식 Claude Code를 설치하고 로그인하세요.\n4. 메일 1~5개를 선택하고 요약을 누르세요. 본문은 선택한 AI로 전송됩니다.\n\nClaude Code에 API 키/다른 공급자가 설정되어 있으면 해당 과금이 적용됩니다. 트윙클은 인증 방식을 변경하거나 토큰을 추출하지 않습니다.",
            "1. Add and sync accounts in Mail → Settings → Accounts.\n2. Load unread mail and allow macOS Automation access to Mail.\n3. Continue with ChatGPT and approve plan usage, or install and sign in to official Claude Code.\n4. Select 1–5 messages and summarize. Their bodies go to the chosen AI.\n\nClaude Code uses its own configured authentication. An API key or cloud provider can incur separate charges. Twinkle does not extract Claude credentials.")
        alert.runModal()
    }

    @objc func loadMail() {
        launch { [self] in
            let process = ChildProcess(); runner = process
            activityStatus.stringValue = text("Mail에서 안 읽은 메일을 가져오는 중…", "Loading unread messages from Mail…")
            messages = try await MailConnection.unread(using: process); table.reloadData()
            activityStatus.stringValue = text("안 읽은 메일 \(messages.count)개 (최대 50개)", "\(messages.count) unread messages (up to 50)")
        }
    }
    @objc func summarizeSelected() {
        let selected = table.selectedRowIndexes.compactMap { messages.indices.contains($0) ? messages[$0] : nil }
        guard (1...5).contains(selected.count) else {
            activityStatus.stringValue = text("메일 1~5개를 선택하세요.", "Select 1–5 messages."); return
        }
        let request = question.stringValue
        launch { [self] in try await summarize(selected, question: request) }
    }
    @MainActor func summarize(_ selected: [MailMessage], question: String) async throws {
        if settings.provider == "chatgpt" && settings.model.isEmpty { try await updateModels() }
        let process = ChildProcess(); runner = process
        var context: [(MailMessage, String)] = []
        activityStatus.stringValue = text("선택한 메일을 읽고 있어요…", "Reading the selected messages…")
        for item in selected { try Task.checkCancellation(); context.append((item, try await MailConnection.body(item.id, using: process))) }
        let prompt = try MailPrompt.make(messages: context, question: question, english: english)
        activityStatus.stringValue = text("AI가 요약하고 할 일을 정리하는 중…", "AI is summarizing and finding next actions…")
        let answer: String
        if settings.provider == "claude" { answer = try await ClaudeCode.summarize(prompt, runner: process) }
        else { answer = try await chatGPT.summarize(prompt, model: settings.model) }
        try Task.checkCancellation(); output.string = answer
        activityStatus.stringValue = text("요약 완료 · 실제 메일에서 내용과 기한을 확인하세요.", "Summary ready · verify details and deadlines in the original mail.")
    }
    @objc func copySummary() {
        guard !output.string.isEmpty else { return }
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(output.string, forType: .string)
    }
    @objc func monitorChanged() {
        settings.monitorMail = monitor.state == .on; save()
        pollTimer?.invalidate(); pollTimer = nil
        if settings.monitorMail {
            UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
            startPolling()
        }
    }
    @objc func autoChanged() { settings.autoSummarize = automatic.state == .on; save() }
    func startPolling() {
        pollTimer?.invalidate()
        pollTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in self?.pollMail() }
        pollMail()
    }
    func pollMail() {
        guard !busy else { return }
        launch { [self] in
            let process = ChildProcess(); runner = process
            let current = try await MailConnection.unread(using: process)
            let previous = settings.seenIDs.map { Set($0) }
            let fresh = previous == nil ? [] : current.filter { !previous!.contains($0.id) }
            settings.seenIDs = Array(Set((settings.seenIDs ?? []) + current.map(\.id))).sorted().suffix(1000).map { $0 }
            save(); messages = current; table.reloadData()
            guard !fresh.isEmpty else { return }
            let notice = UNMutableNotificationContent()
            notice.title = text("트윙클 · 새 메일 \(fresh.count)개", "Twinkle · \(fresh.count) new messages")
            notice.body = text("메뉴 막대 ✦ → 메일 · AI 도우미에서 확인하세요.", "Open ✦ → Mail & AI assistant to review.")
            UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: notice, trigger: nil), withCompletionHandler: nil)
            if settings.autoSummarize {
                try await summarize(Array(fresh.prefix(3)), question: text("핵심 내용과 다음 할 일, 기한을 정리해 줘.", "Summarize key points, next actions, and deadlines."))
            }
        }
    }
    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        DispatchQueue.main.async { [weak self] in self?.show() }; completionHandler()
    }
}
