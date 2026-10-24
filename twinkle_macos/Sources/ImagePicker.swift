import AppKit
import UniformTypeIdentifiers

extension Desktop {
    @objc func openPicker(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String, let mode = Activity(rawValue: raw) else { return }
        pickerActivity = mode
        picker?.close()
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 650, height: 590),
            styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.title = text("상황별 이미지", "Activity images") + " · " + mode.title(preferences.english)
        let content = window.contentView!
        let hint = NSTextField(wrappingLabelWithString: text(
            "이미지를 누르면 저장돼요. 해당 모드를 선택하면 적용됩니다.",
            "Click an image to save it. It appears when you select this activity."))
        hint.frame = NSRect(x: 20, y: 548, width: 610, height: 25)
        hint.textColor = .secondaryLabelColor; content.addSubview(hint)
        let importButton = NSButton(title: text("내 이미지 추가…", "Import image…"), target: self, action: #selector(importImage))
        importButton.frame = NSRect(x: 18, y: 500, width: 180, height: 34); content.addSubview(importButton)
        let resetButton = NSButton(title: text("기본 이미지로", "Restore default"), target: self, action: #selector(resetImage))
        resetButton.frame = NSRect(x: 204, y: 500, width: 180, height: 34); content.addSubview(resetButton)
        let scroll = NSScrollView(frame: NSRect(x: 16, y: 16, width: 618, height: 472))
        scroll.hasVerticalScroller = true; scroll.borderType = .noBorder
        let rows = Int(ceil(Double(library.count) / 5))
        let document = NSView(frame: NSRect(x: 0, y: 0, width: 600, height: CGFloat(rows * 124)))
        for (index, item) in library.enumerated() {
            let button = NSButton(title: preferences.english ? item.Id : item.Title,
                                  target: self, action: #selector(chooseImage(_:)))
            button.frame = NSRect(x: CGFloat(index % 5 * 120 + 4),
                y: CGFloat((rows - 1 - index / 5) * 124 + 4), width: 112, height: 116)
            button.bezelStyle = .regularSquare; button.imagePosition = .imageAbove
            button.font = .systemFont(ofSize: 11); button.imageScaling = .scaleProportionallyDown
            if let image = NSImage(contentsOf: root.appendingPathComponent(item.Preview)) {
                image.size = NSSize(width: 72, height: 72); button.image = image
            }
            button.identifier = NSUserInterfaceItemIdentifier(item.Id)
            button.toolTip = item.Id + " · " + item.Title
            button.setButtonType(.toggle)
            button.state = preferences.choices[mode.rawValue] == item.Id ? .on : .off
            document.addSubview(button)
        }
        scroll.documentView = document; content.addSubview(scroll)
        document.scroll(NSPoint(x: 0, y: document.frame.height))
        window.center(); picker = window
        NSApplication.shared.activate(ignoringOtherApps: true); window.makeKeyAndOrderFront(nil)
    }

    @objc func chooseImage(_ sender: NSButton) {
        guard let id = sender.identifier?.rawValue, library.contains(where: { $0.Id == id }) else { return }
        preferences.choices[pickerActivity.rawValue] = id
        stateStarted = now; pausedAge = 0; save(); tick()
        for case let button as NSButton in sender.superview?.subviews ?? [] {
            button.state = button === sender ? .on : .off
        }
    }

    @objc func resetImage() {
        preferences.choices.removeValue(forKey: pickerActivity.rawValue)
        stateStarted = now; pausedAge = 0; save(); tick()
        picker?.close()
    }

    @objc func importImage() {
        let dialog = NSOpenPanel()
        dialog.allowedContentTypes = [.png, .jpeg, .gif]
        if let apng = UTType(filenameExtension: "apng") { dialog.allowedContentTypes.append(apng) }
        dialog.allowsMultipleSelection = false; dialog.canChooseDirectories = false
        dialog.message = text("PNG·APNG·GIF·JPG, 최대 32 MB / 300 프레임", "PNG, APNG, GIF, JPG — up to 32 MB / 300 frames")
        let selectedMode = pickerActivity
        dialog.begin { [weak self] response in
            guard let self = self, response == .OK, let url = dialog.url else { return }
            do {
                let clip = try Clip(imported: url)
                let directory = self.settingsURL.deletingLastPathComponent().appendingPathComponent("images")
                try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
                let id = "import-" + UUID().uuidString + "." + url.pathExtension.lowercased()
                try FileManager.default.copyItem(at: url, to: directory.appendingPathComponent(id))
                self.cache[id] = clip; self.preferences.choices[selectedMode.rawValue] = id
                self.stateStarted = self.now; self.pausedAge = 0; self.save(); self.tick()
                self.picker?.close()
            } catch {
                let alert = NSAlert()
                alert.messageText = self.text("이미지를 불러오지 못했어요", "Unable to import image")
                alert.informativeText = String(describing: error); alert.runModal()
            }
        }
    }
}
