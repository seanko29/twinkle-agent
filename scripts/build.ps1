$ErrorActionPreference = 'Stop'
$twinkleRepository = Split-Path -Parent $PSScriptRoot
$twinkleDesktop = Join-Path $twinkleRepository 'twinkle_desktop'
& (Join-Path $twinkleDesktop 'src/build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }
$twinkleInfo = [ordered]@{ version='0.8.0'; platform='Windows 10/11, .NET Framework 4.8+'; account_configuration='not included'; api_keys='not included'; ai_mail_summarization='not implemented'; browser_notifications='optional; disabled by default'; gallery_items=47; mail_window='modeless; roaming continues'; executable_sha256=(Get-FileHash -LiteralPath (Join-Path $twinkleDesktop 'TwinkleDesktop.exe') -Algorithm SHA256).Hash.ToLowerInvariant() }
[IO.File]::WriteAllText((Join-Path $twinkleDesktop 'BUILD_INFO.json'),($twinkleInfo | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output 'Built Twinkle Agent 0.8.0. No account configuration or API keys are supplied.'
