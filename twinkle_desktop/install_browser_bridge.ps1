param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$twinkleHostName = 'com.twinkle.desktop'
$twinkleManifestPath = Join-Path $PSScriptRoot 'native_host.json'
$twinkleRegistryPaths = @('HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.twinkle.desktop','HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.twinkle.desktop')
if ($Uninstall) {
    foreach ($twinkleRegistryPath in $twinkleRegistryPaths) {
        if (Test-Path -LiteralPath $twinkleRegistryPath) {
            $twinkleRegistered = (Get-Item -LiteralPath $twinkleRegistryPath).GetValue('')
            if ($twinkleRegistered -eq $twinkleManifestPath) { Remove-Item -LiteralPath $twinkleRegistryPath }
        }
    }
    Write-Output '이 폴더의 브라우저 연결 등록을 해제했습니다.'
    exit
}
$twinkleExtensionId = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'browser_extension_id.txt') -Raw).Trim()
if ($twinkleExtensionId -notmatch '^[a-p]{32}$') { throw '확장 ID가 올바르지 않습니다.' }
$twinkleHost = @{name=$twinkleHostName;description='Twinkle local completion notifications';path=(Join-Path $PSScriptRoot 'TwinkleNativeHost.exe');type='stdio';allowed_origins=@('chrome-extension://' + $twinkleExtensionId + '/')}
[IO.File]::WriteAllText($twinkleManifestPath, ($twinkleHost | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
foreach ($twinkleRegistryPath in $twinkleRegistryPaths) {
    if (Test-Path -LiteralPath $twinkleRegistryPath) {
        $twinkleRegistered = (Get-Item -LiteralPath $twinkleRegistryPath).GetValue('')
        if ($twinkleRegistered -ne $twinkleManifestPath) { throw ('다른 트윙클 설치가 등록되어 있습니다: ' + $twinkleRegistered) }
    }
    New-Item -Path $twinkleRegistryPath -Force | Out-Null
    Set-Item -LiteralPath $twinkleRegistryPath -Value $twinkleManifestPath
}
Write-Output 'Chrome/Edge의 현재 Windows 사용자에게 알림 통로를 등록했습니다.'
Write-Output ('확장 프로그램 페이지에서 이 폴더를 로드하세요: ' + (Join-Path $PSScriptRoot 'browser_extension'))
