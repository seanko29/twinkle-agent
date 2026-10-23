# macOS preview · KOR / ENG

Twinkle Agent 0.9.0 preview adds a native AppKit companion for **macOS 12+**, with one universal app for **Apple Silicon and Intel**. No Python, .NET, or API key is required to run the packaged app.

## 한국어 · 설치

1. 저장소의 **Actions → macOS app**에서 성공한 실행을 엽니다. 아래 **Artifacts**의 `TwinkleAgent-macOS-macos-15`를 받습니다. GitHub 로그인이 필요하며 파일은 30일간 보관됩니다. 일반 Releases 배포는 아직 하지 않았습니다.
2. 받은 파일에서 `TwinkleAgent_macOS_universal.dmg`를 열고 `Twinkle Agent.app`을 **Applications** 폴더로 드래그합니다. 또는 앱 ZIP을 압축 해제해 응용 프로그램 폴더에 넣습니다.
3. 앱을 실행하면 캐릭터와 메뉴 막대의 **✦** 아이콘이 나타납니다. Dock에는 별도 아이콘이 생기지 않습니다.
4. 캐릭터 클릭/우클릭 또는 ✦ 메뉴에서 모드를 고릅니다. Mac UI도 **한국어 / English**를 전환할 수 있습니다.

이번 CI 빌드는 임시(ad-hoc) 서명된 **시험 배포본**입니다. Apple Developer ID 서명·공증은 되어 있지 않아 다운로드한 앱이 Gatekeeper에 의해 차단될 수 있습니다. 일반 사용자용 배포 전에는 아래 서명·공증 절차가 필요합니다. [Apple의 앱 실행 안내](https://support.apple.com/102445)

## 조작과 지원 범위

| 기능 | macOS preview |
|---|---|
| 캐릭터 이동 | 작업 영역 안에서 돌아다니기, 드래그 이동, 화면 구성 변경 시 위치 보정 |
| 집중·휴식·회의·퇴근 | 원본 동작과 경과 타이머, 일시정지·다시 시작 |
| 크기 | 메뉴 또는 Control / Command + 스크롤, 80~320px |
| 이미지 | 47개 애니메이션 선택, 상황별 저장, GIF·APNG·PNG·JPG 추가 |
| 개인 이미지 제한 | 파일당 32 MB / 300프레임, 디코딩은 프레임당 최대 512px |
| 사용법·갤러리 | KOR / ENG, 전체 갤러리를 오프라인으로 포함 |
| 숨기기·종료 | 메뉴 막대 ✦에서 숨기기/보이기, 종료 |
| 메일 계정 연동 | Mac 버전에 미포함. Windows용 연결 코드는 기존 경로에 유지 |
| Chrome/Edge 완료 알림 | Windows 전용. Mac 브라우저 브리지는 미구현 |
| AI 메일 요약 | 두 플랫폼 모두 미구현 |

집중·휴식·회의 중에는 제자리에서 애니메이션을 재생합니다. **퇴근**을 선택하면 인사 후 다시 돌아다닙니다. 설정 창은 별도 창으로 열리며 전체 앱을 멈추지 않습니다.

설정은 `~/Library/Application Support/TwinkleAgent/settings.json`, 추가 이미지는 그 아래 `images/`에 저장됩니다. 앱 번들 안에 사용자 데이터를 쓰지 않아 Applications 폴더에서도 동작하고, 앱을 교체해도 개인 설정이 유지됩니다. 계정·비밀번호·API 키를 수집하거나 포함하지 않습니다.

## English · install and use

1. Open a successful **Actions → macOS app** run and download the `TwinkleAgent-macOS-macos-15` artifact. GitHub sign-in is required; artifacts expire after 30 days. This preview has not been published to Releases.
2. Open `TwinkleAgent_macOS_universal.dmg` and drag **Twinkle Agent.app** to **Applications**, or extract the app ZIP there.
3. Launch the app. Look for the character and the **✦** menu bar icon; there is no Dock icon.
4. Click/right-click the character or use ✦ for activities, pause/resume, sizing, image selection, hide/show, language, and quit. Drag to move. Control or Command + scroll resizes.

Focus, Rest, and Meeting animate in place and track elapsed time. Off work plays a farewell then resumes roaming. The image picker is modeless. All 47 animations and the bilingual guide/gallery are bundled offline. Personal GIF/APNG/PNG/JPG imports are limited to 32 MB and 300 frames; frames are decoded at up to 512px.

Settings and imports are stored in `~/Library/Application Support/TwinkleAgent/`, outside the app bundle. No credentials are bundled or collected. **Mac mail integration, the browser notification bridge, and AI summaries are not included.**

CI builds are **ad-hoc signed previews without Developer ID signing or Apple notarization**. Downloaded copies may be blocked by Gatekeeper. See [Apple's guidance](https://support.apple.com/102445). General distribution requires the signing and notarization steps below.

## Build · 빌드

On a Mac with Xcode Command Line Tools and Python 3:

```bash
bash scripts/build_macos.sh
```

The script compiles both `arm64` and `x86_64` for macOS 12, combines them with `lipo`, assembles a clean `.app`, creates its icon from existing artwork, verifies its signature, runs core/resource checks and a native UI smoke test, then creates DMG/ZIP/checksums in `dist/macos/`. It does not modify any Windows source.

The workflow runs on both an Apple Silicon and an Intel GitHub-hosted Mac. Each executes the universal app natively on its own architecture. Resource validation covers all eight activity clips and all 47 library entries / 555 frames. The UI smoke test covers transparent-window creation, activity changes, pause/resume, resize, hide/show, image choice, the modeless picker, and PNG rendering.

The minimum deployment target is macOS 12; CI runs on macOS 15. Physical-Mac checks of multiple monitors, full-screen spaces, drag gestures, imported APNG timing, sleep/wake, and a quarantined download remain necessary. CI smoke tests do not establish those behaviors.

## Developer ID signing / Apple 공증

For general distribution, obtain a Developer ID Application certificate with its private key in the building Mac's login keychain. Configure a `notarytool` keychain profile interactively on that Mac. Never commit certificate files, account credentials, or app-specific passwords.

```bash
xcrun notarytool store-credentials twinkle-notary

CODESIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
NOTARY_PROFILE="twinkle-notary" \
bash scripts/build_macos.sh
```

With those values the build uses hardened-runtime signing, notarizes and staples the app, signs/notarizes/staples the DMG, and recreates the app ZIP after stapling. The default CI workflow does not read signing secrets, upload to Apple, or automatically publish a release. Certificates and an Apple account were not available for verification of this signing path.

[Apple Developer ID](https://developer.apple.com/developer-id/) · [Apple notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow)

Artwork sources and rights remain documented in [ASSET_NOTICE.md](../ASSET_NOTICE.md).
