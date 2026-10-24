# Twinkle Agent ✦

A small Windows / macOS desktop companion that wanders quietly, cheers you on, and takes a break with you. Choose a look from the included animation gallery.

<a href="README.ko.md"><img src="docs/kor.svg" alt="한국어 안내" width="76" height="32"></a> <a href="README.en.md"><img src="docs/eng.svg" alt="English guide" width="76" height="32"></a>

[**KOR / ENG user guide**](https://seanko29.github.io/twinkle-agent/) · [**Windows downloads**](https://github.com/seanko29/twinkle-agent/releases/tag/v0.8.0) · [Image gallery](https://seanko29.github.io/twinkle-agent/twinkle_twinkle/)

![Strawberry-hat walking preview](twinkle_desktop/walk_preview.gif)

## macOS preview · Apple Silicon / Intel

The native macOS 12+ app supports roaming, drag movement, activity timers, 47 animations, personal image import, and menu bar controls. Its UI switches between Korean and English.

- [Mac installation, controls, builds, and signing](docs/macOS.md)
- [Download macOS 0.10.0 preview](https://github.com/seanko29/twinkle-agent/releases/tag/v0.10.0): download the universal DMG/ZIP from Releases. No GitHub sign-in is required.
- One universal app includes Apple Silicon and Intel code. CI executes it on both kinds of Mac.
- Current downloads are ad-hoc signed previews without Developer ID signing or notarization. The build script also supports the general-distribution signing path.
- Mac connects Apple Mail with a ChatGPT plan or official Claude Code for summaries and next actions. See [Mail and subscription AI setup](docs/Mail-AI.md). The browser notification bridge remains Windows-only; Windows AI summaries are not implemented.

```bash
# macOS + Xcode Command Line Tools + Python 3
bash scripts/build_macos.sh
```

The installation and connection instructions below apply to the **existing Windows version**.

## Install and use

1. Download `TwinkleDesktop_Setup.exe` from Releases. Requires Windows 10/11 and .NET Framework 4.8 or later. No administrator privileges required.
2. Click the character to select **Focus (집중), Rest (휴식), Meeting (회의), or Off work (퇴근)**. Focus, Rest, and Meeting animate in place.
3. Drag to move. Right-click for the size slider, or use **Ctrl + mouse wheel**. Default: 144px; range: 80–320px.
4. Use **상황별 이미지 바꾸기** to select one of 47 animations or import GIF, APNG, PNG, or JPG files.
5. Right-click **사용법 / Guide (KOR · ENG)** or **이미지 갤러리 / Gallery** to open the offline guide or gallery.

Pause and resume preserve the activity timer; animation restarts immediately. Mail settings do not block roaming. Double-click the notification-area Twinkle icon to restore a hidden character.

The installer and portable ZIP include the bilingual guide and full offline gallery. The desktop UI currently uses Korean labels. Installers are not code-signed.

## Connections and privacy

- **No accounts, passwords, API keys, or registered Google/Microsoft OAuth clients are included.** Fresh installations start disconnected.
- Gmail, Naver, Outlook, and IMAP integration code is included, but you must supply your own provider configuration. Google/Microsoft sign-in remains disabled until your own app is registered.
- Naver requires IMAP access and an app password. Live provider login and incoming delivery are not verified by this public release's checks.
- Mail integration reads metadata only. Reading bodies/attachments and **AI mail summarization with GPT, Claude, or Gemini are not implemented.**
- The optional Chrome/Edge extension detects web-response completion from UI state. Detection is off by default; chat contents and API keys are not sent. Live authenticated AI sites have not been verified, and site changes can affect detection.
- Size preferences use `settings.json`, imported artwork uses `user_images/`, and accounts you connect later use `user_mail/accounts.dat`, encrypted with Windows DPAPI for the current user. They are excluded from Git and releases. Existing personal data survives updates and uninstall.

See the app's `메일_연결_안내.txt`, `알림_연동_안내.txt`, and `app_config/README.txt` for optional connection setup.

## Build from source

Run on Windows. Runtime DLLs and license records are vendored, so compilation requires no API keys or service accounts.

```powershell
python scripts/verify_public.py
powershell -NoProfile -File scripts/build.ps1
./twinkle_desktop/TwinkleDesktop.exe
```

Create a portable ZIP and Windows installer:

```powershell
python scripts/package.py --inno "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Outputs go to `dist/`. Install Inno Setup 6 separately; omit `--inno` to build only the portable ZIP. Packaging copies an explicit allowlist and never includes account configuration.

## Repository layout

| Path | Contents |
|---|---|
| `index.html` | Interactive KOR / ENG user guide |
| `twinkle_twinkle/` | 47 originals, 555 frames, gallery, and provenance |
| `twinkle_macos/` | Native AppKit app and universal macOS build |
| `twinkle_desktop/src/` | WPF app, decoder, mail, local notifications, and installer |
| `twinkle_desktop/browser_extension/` | Optional Chrome/Edge completion notifications |
| `scripts/` | Build, publication checks, and clean packaging |

## Artwork and attribution

An unofficial fan project, not affiliated with POP MART. Character artwork and trademarks belong to their respective owners. This repository grants no new rights to third-party artwork. Assets were collected from public third-party uploads; official provenance and redistribution permission are unverified. Original URLs, uploader labels, and hashes remain in `twinkle_twinkle/catalog.json`. [Artwork notice](ASSET_NOTICE.md)

No AI-generated images are used. The desktop walk keeps the source strawberry character's upper body fixed and animates small foot movements. Dependency licenses are in `twinkle_desktop/third_party/`.
