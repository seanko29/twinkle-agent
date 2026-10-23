# Twinkle Agent ✦

바탕 화면을 조용히 돌아다니는 Windows / macOS용 트윙클 친구입니다. 집중·휴식·회의·퇴근에 맞춰 표정을 바꾸고, 원본 이미지 갤러리에서 원하는 모습을 고를 수 있습니다.

<a href="README.ko.md"><img src="docs/kor.svg" alt="한국어 안내" width="76" height="32"></a> <a href="README.en.md"><img src="docs/eng.svg" alt="English guide" width="76" height="32"></a>

[**KOR / ENG 사용 안내**](https://seanko29.github.io/twinkle-agent/) · [**Windows 다운로드**](https://github.com/seanko29/twinkle-agent/releases/latest) · [이미지 갤러리](https://seanko29.github.io/twinkle-agent/twinkle_twinkle/)

![딸기 모자 걷기](twinkle_desktop/walk_preview.gif)

## macOS 시험 배포 · Apple Silicon / Intel

macOS 12 이상에서 실행하는 네이티브 앱을 추가했습니다. 캐릭터 이동·드래그·집중/휴식/회의 타이머·47개 이미지 선택·개인 이미지 추가·메뉴 막대를 지원합니다. Mac UI도 한국어/영어를 전환할 수 있습니다. 0.10.0 preview부터 메일·구독 AI 도우미를 포함합니다.

- [Mac 설치·조작·빌드·서명 안내](docs/macOS.md)
- [macOS 앱 빌드와 다운로드](https://github.com/seanko29/twinkle-agent/actions/workflows/macos.yml): 성공한 실행의 Artifacts에서 DMG/ZIP을 받습니다. GitHub 로그인 필요, 30일 보관.
- 하나의 universal 앱에 Apple Silicon과 Intel 코드를 함께 포함합니다. CI는 두 종류의 Mac에서 각각 실행합니다.
- 현재는 Apple Developer ID 서명·공증 전 시험 배포본입니다. 일반 배포용 서명·공증 경로도 빌드 스크립트에 준비되어 있습니다.
- Mac은 Apple Mail + ChatGPT 구독 / Claude Code를 연결해 메일 요약과 할 일 추출을 지원합니다. [메일·구독 AI 연결 안내](docs/Mail-AI.md). Chrome/Edge 완료 알림은 Windows 전용이며 Windows AI 요약은 미구현입니다.

```bash
# macOS + Xcode Command Line Tools + Python 3
bash scripts/build_macos.sh
```

아래 설치·연결 설명은 **기존 Windows 버전**에 해당합니다.

## 설치와 조작

1. Releases의 `TwinkleDesktop_Setup.exe`를 설치합니다. Windows 10/11, .NET Framework 4.8 이상이 필요하며 관리자 권한은 필요 없습니다.
2. 캐릭터를 클릭해 **집중 · 휴식 · 회의 · 퇴근**을 선택합니다. 집중·휴식·회의 중에는 제자리에서 동작합니다.
3. 드래그로 이동하고, 우클릭 슬라이더 또는 **Ctrl + 마우스 휠**로 크기를 조절합니다. 기본 144px, 범위 80~320px입니다.
4. **상황별 이미지 바꾸기**에서 47개 이미지 중 선택하거나 GIF·APNG·PNG·JPG를 추가합니다.
5. 우클릭의 **사용법 / Guide (KOR · ENG)**와 **이미지 갤러리 / Gallery**로 오프라인 안내와 갤러리를 엽니다.

휴식 일시정지 → 다시 시작 시 동작이 즉시 이어집니다. 메일 설정 창을 열어 둬도 자유 이동은 계속됩니다. 앱을 숨겼다면 Windows 알림 영역의 트윙클 아이콘을 더블 클릭합니다.

설치 파일과 포터블 ZIP 모두 한국어·영어 전환 안내 및 전체 이미지 갤러리를 포함합니다. 앱 자체 UI는 현재 한국어입니다. 설치 파일은 코드 서명되지 않았습니다.

## 연결 기능과 개인정보

- **공개본에는 계정, 비밀번호, API 키, Google/Microsoft OAuth 앱 등록 값이 없습니다.** 신규 설치는 계정 미연결 상태로 시작합니다.
- Gmail·네이버·Outlook·회사 IMAP 연결 코드가 포함되지만, 공급자 설정은 사용자가 직접 준비해야 합니다. Gmail/Microsoft 버튼은 본인의 앱 등록 값을 넣기 전에는 비활성화됩니다.
- 네이버는 IMAP 사용 설정과 앱 비밀번호가 필요합니다. 실제 공급자 계정의 로그인·메일 수신은 이 공개 배포 검증에 포함되지 않습니다.
- 메일은 메타데이터만 확인합니다. 본문·첨부파일 읽기 및 GPT·Claude·Gemini **메일 요약은 아직 구현되지 않았습니다.**
- 선택 사항인 Chrome/Edge 확장은 AI 웹 UI에서 응답 종료 신호를 감지합니다. 감지는 기본으로 꺼져 있으며 대화 내용이나 API 키를 전달하지 않습니다. 실제 AI 로그인 환경의 감지는 검증되지 않았고 사이트 변경의 영향을 받습니다.
- 로컬 크기 설정은 `settings.json`, 개인 이미지는 `user_images/`, 이후 연결한 계정은 Windows DPAPI로 암호화된 `user_mail/accounts.dat`에 저장됩니다. Git과 공개 패키지에서 제외됩니다. 기존 사용자 데이터는 업데이트·제거 후에도 보존됩니다.

선택 연결 방법은 앱 폴더의 `메일_연결_안내.txt`, `알림_연동_안내.txt`, `app_config/README.txt`에 있습니다.

## 소스에서 빌드

Windows PowerShell에서 실행합니다. 저장소에 런타임 DLL과 라이선스 기록이 포함되어 있어 빌드 시 계정·API 키는 필요하지 않습니다.

```powershell
python scripts/verify_public.py
powershell -NoProfile -File scripts/build.ps1
./twinkle_desktop/TwinkleDesktop.exe
```

포터블 ZIP과 설치 파일 생성:

```powershell
python scripts/package.py --inno "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

패키지 결과는 `dist/`에 생성됩니다. Inno Setup 6는 별도 설치하며, `--inno`를 생략하면 포터블 ZIP만 만듭니다. 패키징은 허용된 파일만 복사하고 계정·연결 설정을 넣지 않습니다.

## 구성

| 경로 | 내용 |
|---|---|
| `index.html` | KOR / ENG 전환 사용 안내 |
| `twinkle_twinkle/` | 47개 원본 애니메이션, 555개 프레임, 갤러리와 출처 목록 |
| `twinkle_macos/` | macOS AppKit 앱, universal 빌드·설치 안내 |
| `twinkle_desktop/src/` | WPF 앱·이미지 디코더·메일·로컬 알림·설치 소스 |
| `twinkle_desktop/browser_extension/` | 선택형 Chrome/Edge 완료 알림 확장 |
| `scripts/` | 빌드·개인정보 검사·공개 패키징 |

## 이미지와 권리

POP MART와 관련 없는 비공식 팬 프로젝트입니다. 캐릭터 이미지·상표는 원 권리자의 것이며, 이 저장소는 이미지의 새로운 이용 허가를 부여하지 않습니다. 공개된 제3자 업로드에서 수집했고 공식 출처 여부와 재배포 허가는 검증되지 않았습니다. 원본 URL·업로더 표시·해시는 `twinkle_twinkle/catalog.json`에 보존되어 있습니다. [이미지 출처 및 권리 안내](ASSET_NOTICE.md)

AI로 이미지를 생성하지 않았습니다. 데스크탑 걷기는 기존 딸기 모자 이미지의 상체를 고정하고 발 부분만 작게 변형한 동작입니다. 외부 라이브러리의 라이선스는 `twinkle_desktop/third_party/`에서 확인할 수 있습니다.
