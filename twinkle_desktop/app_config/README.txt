Optional provider setup / 선택 연결 설정

No real OAuth client IDs, client secrets, API keys or accounts are included.
실제 OAuth 앱 등록 값·API 키·계정 정보는 포함하지 않습니다.

Gmail: Register your own Google desktop OAuth app, enable Gmail API, and configure the gmail.metadata scope and audience. Import the desktop JSON in 메일 연결 → Gmail → 개발자 설정. Your local google_desktop.json is ignored by Git and excluded from releases. Gmail testing apps may restrict accounts and require reauthentication.

Outlook: Register your own Microsoft public-client app with the desired personal/organization account audience. Enable public client flows and delegated User.Read / Mail.ReadBasic permissions. Save the client ID in 메일 연결 → Outlook → 개발자 설정. No server client secret is needed. Tenant policy may require administrator approval.

Do not commit provider settings, access tokens, refresh tokens, mail vaults or app passwords. Packaging never copies these local settings.
본문 열람·AI 메일 요약 연결은 아직 구현되지 않았습니다.
