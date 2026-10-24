# Mail + subscription AI · macOS 0.10.0 preview

## 한국어

메뉴 막대 **✦ → 메일 · AI 도우미**를 엽니다.

1. **Mail 앱 열기 / 계정 추가**를 누릅니다. Apple Mail의 **설정 → 계정**에서 Gmail, Outlook, 네이버, iCloud, 회사 메일 등 지원되는 계정을 추가하고 동기화합니다. 트윙클에 메일 비밀번호나 공급자 API 키를 다시 입력할 필요가 없습니다. 계정 지원 범위와 로그인 조건은 Apple Mail 및 메일 공급자 설정을 따릅니다.
2. **안 읽은 메일 가져오기**를 누르고 macOS가 요청하는 Mail 자동화 접근을 허용합니다. 권한을 거절했다면 **시스템 설정 → 개인정보 보호 및 보안 → 자동화**에서 변경할 수 있습니다. 받은편지함의 안 읽은 메일을 최대 50개 가져옵니다.
3. 아래 중 사용할 AI를 연결합니다.
4. 메일 **1~5개**를 선택하고 요청을 입력한 뒤 **선택 메일 요약**을 누릅니다. 예: `핵심 내용과 할 일, 기한을 알려줘`, `정중한 답장 초안을 만들어줘`. 결과에 원본 메일 ID가 표시됩니다. 답장 초안은 텍스트로만 만들며, 메일 발송 기능은 없습니다.

### ChatGPT 구독

**Continue with ChatGPT**를 누르면 시스템 브라우저에서 로그인하고 **ChatGPT 구독 사용** 권한을 승인합니다. 오픈소스 앱용 Sign in with ChatGPT의 동적 클라이언트 등록·PKCE·OIDC 흐름을 사용합니다. 파트너 API 키나 앱에 공유된 client secret은 없습니다.

연결되면 계정에 실제로 허용된 모델 목록을 불러옵니다. 사용할 모델을 선택하세요. 로그인만 허용하고 구독 사용 권한을 승인하지 않은 경우에는 요약 요청을 실행하지 않습니다. 사용량은 본인 플랜의 한도와 앱별 제한을 따르며, 연결한다고 별도의 사용량이 생기지 않습니다. 플랜/지역/워크스페이스 조건에 따라 승인이 거부될 수 있습니다.

토큰은 macOS Keychain에 보관합니다. 액세스 토큰 만료 시 갱신하며, 연결 해제 시 서버 세션 폐기를 시도하고 로컬 토큰을 삭제합니다. 서버 폐기를 확인하지 못하면 이를 표시합니다. 완전한 접근 해제와 사용량 확인은 ChatGPT 설정에서도 할 수 있습니다. 이 연결은 기존 ChatGPT 대화나 메모리 접근 권한을 주지 않습니다.

### Claude 구독

공식 **Claude Code**를 별도로 설치하고 **Claude Code 로그인**을 누릅니다. 트윙클은 Anthropic이 배포한 실행 파일을 변경하지 않고, 사용자가 직접 로그인한 환경에서 실행합니다. Claude 웹사이트의 쿠키나 OAuth 토큰을 추출하지 않습니다. **연결 확인**에 실제 인증 방식이 표시됩니다.

**Claude Code 구독** 모드는 실제 인증 방식을 확인하고 구독 인증인 경우에만 요약합니다. `ANTHROPIC_API_KEY`, API 키 도우미 또는 클라우드 공급자 인증으로 되어 있다면 구독 모드에서 자동으로 API 과금으로 넘어가지 않습니다. 별도 과금을 원할 때만 AI 선택에서 **Claude Code API/클라우드 과금**을 직접 고르세요. Claude Code의 공식 인증 수단을 변경하거나 대체하지 않습니다. 앱 제공자는 Anthropic의 Claude Code 제품 제공 조건을 준수해야 합니다.

요약은 공식 `claude -p` 경로를 사용합니다. 개인 메일이 명령행 인자에 노출되지 않도록 본문은 표준 입력으로 보냅니다. 최신 Claude Code의 `--safe-mode`, 도구 비활성화, 외부 MCP 비활성화, 세션 미저장 옵션을 사용합니다. 오래된 CLI에서 옵션 오류가 나면 Claude Code를 업데이트하세요. 이 옵션은 인증을 유지하면서 사용자 정의 훅·명령·프로젝트 설정을 요약 작업에서 분리합니다.

### 자동 확인·요약

- **새 메일 확인**: 기본 꺼짐. 켜면 1분마다 메일 메타데이터를 확인하고 새 메일 수를 알립니다. 첫 확인은 기준 목록만 만들며 기존 안 읽은 메일 전체를 자동 요약하지 않습니다.
- **새 메일 본문을 AI에 보내 자동 요약**: 기본 꺼짐. 켜면 이후 발견된 새 메일 중 한 번에 최대 3개를 선택한 AI로 보냅니다. 요약은 도우미 창에서 확인합니다. 공급자 사용량이 소모됩니다.
- 수동 요약은 버튼을 누른 선택 메일만 전송합니다. 메일 본문은 각 메일당 최대 16,000자로 제한하며 첨부파일은 전송하지 않습니다. 긴 메일은 잘릴 수 있습니다.
- 메일을 읽음 처리하거나 삭제·이동·발송하지 않습니다. 요약과 본문은 트윙클에서 디스크에 보관하지 않습니다. 연결 설정과 확인한 메일 ID는 Application Support에, ChatGPT 인증 정보는 Keychain에 저장됩니다. Claude Code 및 AI 서비스의 자체 데이터 처리 정책은 별도로 적용됩니다.

메일의 지시문은 자료로 취급합니다. ChatGPT 요청에는 실행 도구를 제공하지 않으며 Claude 요약 작업에서도 도구를 비활성화합니다. AI가 메일에 적힌 명령을 실행하거나 답장을 실제로 보낼 수 없습니다. 요약의 내용·시간대·기한은 원문과 대조하세요.

Aside는 로그인된 웹사이트를 조작하는 별도 브라우저 에이전트입니다. 이 버전의 트윙클은 Apple Mail 읽기와 AI 요약을 연결하며, 범용 브라우저 자동화나 모든 서비스 연결을 포함하지 않습니다.

## English

Open **✦ → Mail & AI assistant**. Add and sync your accounts in Apple Mail, then load unread mail and grant macOS Automation access. Select 1–5 messages, enter a request, and summarize. The app supports summaries, actions/deadlines, and reply drafts as text; it cannot send mail.

**ChatGPT:** Continue with ChatGPT and authorize plan usage. The app uses the official OSS dynamic-registration OAuth/PKCE/OIDC flow, validates signed identity tokens and granted scopes, stores credentials in Keychain, and lists models available to the selected account. Inference uses the public Responses API with `store: false` and `stream: true`, and only accepts a completed response. Your own plan eligibility, usage limits, and app caps apply. No partner key, shared client secret, or access to existing ChatGPT conversations is included.

**Claude:** Install official Claude Code and sign in using its built-in authentication. Twinkle runs the unmodified CLI and sends message context over stdin. It does not extract browser cookies or Claude credentials. Subscription mode checks the actual authentication method and refuses implicit API/cloud billing. To use your own API/cloud configuration, explicitly select **Claude Code API/cloud billing**. Use Check connection to see the authentication method. Summary runs use safe mode, no tools/MCP, and no session persistence; update Claude Code if your version does not support those options. App providers must follow Anthropic's product-distribution terms.

Mail polling and automatic summarization are separate, off-by-default options. Polling uses a first-run baseline. Automatic summaries send up to three newly discovered messages per poll. Manual summaries send only selected messages. Bodies are capped at 16,000 characters per message; attachments are not sent. Mail is never marked read, moved, deleted, or sent. Twinkle stores no message bodies or summaries on disk. Provider and CLI data policies still apply.

This connects Mail and AI summaries; it does not implement Aside or general browser automation.

## Validation status / 검증 범위

Native builds and synthetic checks cover PKCE vectors, callback state/client validation, RSA-signed JWT verification and tampering, scope enforcement, completed/interrupted/failed Responses streams, request fields, subprocess IO, a real loopback callback listener, and the AppKit assistant window. Test data is synthetic; no personal mailbox or AI provider account is used in CI.

**Live ChatGPT authorization and plan inference, live Claude subscription inference, real Mail account sync/Automation permission, and signed/notarized Gatekeeper behavior require verification by a user on a Mac.** Passing synthetic checks does not establish those account-specific behaviors. Developer ID signing/notarization remains separate from AI authentication.

## Official references

- [OpenAI: OSS sign-in and registration](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)
- [OpenAI: models and completed inference](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
- [OpenAI: accounts, refresh and revocation](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions)
- [Anthropic: offering Claude Code in products](https://code.claude.com/docs/en/legal-and-compliance)
- [Anthropic: CLI reference](https://code.claude.com/docs/en/cli-reference)
- [Aside: browser architecture and subscription options](https://aside.com/)
