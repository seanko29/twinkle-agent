#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef ReleaseDir
  #error ReleaseDir is required
#endif

[Setup]
AppId={{8336A57C-1469-48B2-B230-E22A344EF712}
AppName=트윙클 데스크탑
AppVersion=0.8.0
AppVerName=트윙클 데스크탑 0.8.0
DefaultDirName={localappdata}\Programs\TwinkleDesktop
DefaultGroupName=트윙클 데스크탑
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.17763
WizardStyle=modern
WizardSizePercent=110
SetupIconFile={#PayloadDir}\assets\twinkle.ico
UninstallDisplayIcon={app}\TwinkleDesktop.exe
OutputDir={#ReleaseDir}
OutputBaseFilename=TwinkleDesktop_Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\TwinkleDesktopPortable_20261022
UsePreviousTasks=yes
UsePreviousAppDir=yes
VersionInfoVersion=0.8.0.0

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 트윙클 바로 가기 만들기"; GroupDescription: "바로 가기:"
Name: "autostart"; Description: "Windows에 로그인할 때 트윙클 시작하기"; GroupDescription: "시작 설정:"; Flags: unchecked

Name: "browserbridge"; Description: "Chrome/Edge 로컬 알림 통로 등록 (선택)"; GroupDescription: "선택 연결:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\트윙클 데스크탑"; Filename: "{app}\TwinkleDesktop.exe"
Name: "{group}\처음 사용 안내"; Filename: "{app}\TwinkleDesktop.exe"; Parameters: "--welcome"
Name: "{autodesktop}\트윙클 데스크탑"; Filename: "{app}\TwinkleDesktop.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TwinkleDesktop"; ValueData: """{app}\TwinkleDesktop.exe"""; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "TwinkleDesktop"; Tasks: not autostart; Flags: deletevalue

[Run]
Filename: "{app}\TwinkleDesktop.exe"; Parameters: "--register-browser"; Flags: runhidden waituntilterminated; Check: IntegrationEnabled; Tasks: browserbridge
Filename: "{app}\TwinkleDesktop.exe"; Parameters: "--welcome"; Description: "트윙클 시작하기"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\TwinkleDesktop.exe"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitTwinkle"
Filename: "{app}\TwinkleDesktop.exe"; Parameters: "--unregister-browser"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveBrowserBridge"

[Code]
function IntegrationEnabled: Boolean;
begin
  Result := ExpandConstant('{param:NOINTEGRATION|0}') <> '1';
end;

function InitializeSetup: Boolean;
var
  FrameworkRelease: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', FrameworkRelease) and (FrameworkRelease >= 528040);
  if not Result then
    MsgBox('트윙클은 Microsoft .NET Framework 4.8 이상이 필요합니다.' + #13#10 + 'Windows 업데이트 후 설치를 다시 실행해 주세요.' + #13#10 + '공식 다운로드: https://dotnet.microsoft.com/download/dotnet-framework/net48', mbInformation, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  AppFile: String;
begin
  Result := '';
  AppFile := ExpandConstant('{app}\TwinkleDesktop.exe');
  if FileExists(AppFile) then begin
    Exec(AppFile, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    Sleep(900);
  end;
end;
