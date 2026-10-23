param(
    [Parameter(Mandatory=$true)][string]$Title,
    [string]$Source = '외부 작업',
    [string]$Message = ''
)
$ErrorActionPreference = 'Stop'
$twinkleNotifyFile = Join-Path ([IO.Path]::GetTempPath()) ('twinkle-notify-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
    @{source=$Source;title=$Title;message=$Message} | ConvertTo-Json | Set-Content -LiteralPath $twinkleNotifyFile -Encoding UTF8
    $twinkleSender = Start-Process -FilePath (Join-Path $PSScriptRoot 'TwinkleDesktop.exe') -ArgumentList @('--notify-json', ('"' + $twinkleNotifyFile + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($twinkleSender.ExitCode -ne 0) { throw '알림을 보내지 못했습니다. 캐릭터가 실행 중인지 확인해 주세요.' }
}
finally { if (Test-Path -LiteralPath $twinkleNotifyFile) { Remove-Item -LiteralPath $twinkleNotifyFile } }
