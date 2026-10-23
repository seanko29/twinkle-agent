# Public release review · 0.8.0

Reviewed on 2026-10-23. 공개용 복사본에서 검토했습니다.

## Changes / 변경

- Removed inherited provider configuration, account vaults, imported personal images, size preferences, absolute user paths, generated browser-host registration, and historical build metadata from the publication inputs.
- Published clean application source and existing third-party artwork with its source records. No new artwork was generated.
- Added an offline KOR / ENG guide, translated gallery controls and descriptions, language-selection persistence, and guide/gallery entries in both desktop menus.
- Retained the modeless mail-settings fix: opening mail settings does not stop natural roaming or foot animation.
- Made browser detection and installer browser-bridge registration explicitly opt-in.
- Added file exclusions, publication checks, and allowlisted release packaging. Git commits use a GitHub noreply identity.

## Validation / 검증

- Native build, core controls, elapsed timer, all activity animations, pause/resume, import persistence, and all 47 native gallery items passed existing regression checks.
- Real WPF mail-window regression passed: natural roaming and foot animation continue with settings open; repeated open reuses one window; close/reopen and returning to roaming work.
- Synthetic OAuth/HTTP and actual MailKit against a synthetic local IMAP server passed; no real mailbox was used.
- Browser guide: Korean/English switching, gallery navigation, language persistence after reload, all-47 display, and search checked. No broken images were observed among loaded cards; all local catalog references were also checked on disk.
- Fresh Windows installer includes the offline guide and all gallery data without any account configuration; installed movement regression and uninstall passed.
- Publication scanner checks for credential patterns, personal email addresses, local home paths, and forbidden runtime/configuration files. The original private configuration values were separately compared against repository inputs and decompressed release contents before upload.
- Optional extension test passed: off by default, explicit opt-in, origin allowlist, and no conversation content forwarded.

## Feature status / 현재 상태

Live Gmail/Naver/Outlook login and incoming delivery are not verified in this release. Users must supply their own provider setup. AI mail summarization is not implemented. Live authenticated ChatGPT/Claude/Gemini web completion detection is not verified. The desktop UI currently uses Korean; the guide and gallery support KOR / ENG. Third-party artwork permissions remain as documented in ASSET_NOTICE.md.
