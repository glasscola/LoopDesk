#ifndef AppBinary
  #define AppBinary "..\downloads\오토 매크로.exe"
#endif
#ifndef GuideFile
  #define GuideFile "..\docs\오토 매크로-사용설명서-v1.1.pdf"
#endif
[Setup]
AppId={{D7F5141A-5F53-4F24-B423-938B07F92AB0}
AppName=오토 매크로
AppVersion=5.0.0
AppPublisher=glasscola
AppPublisherURL=https://github.com/glasscola/LoopDesk
AppSupportURL=https://github.com/glasscola/LoopDesk/issues
AppUpdatesURL=https://github.com/glasscola/LoopDesk/releases
DefaultDirName={localappdata}\Programs\오토 매크로
DefaultGroupName=오토 매크로
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\outputs
#ifdef DirectSetup
UseSetupLdr=no
OutputBaseFilename=오토 매크로-Install-v5
#else
OutputBaseFilename=오토 매크로-Setup-v5
#endif
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\오토 매크로.exe
AppMutex=Local\LoopDesk.Main
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로가기 만들기"; Flags: unchecked

[Files]
Source: "{#AppBinary}"; DestDir: "{app}"; DestName: "오토 매크로.exe"; Flags: ignoreversion
Source: "{#GuideFile}"; DestDir: "{app}"; DestName: "오토 매크로-사용설명서-v1.1.pdf"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\오토 매크로"; Filename: "{app}\오토 매크로.exe"
Name: "{autodesktop}\오토 매크로"; Filename: "{app}\오토 매크로.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LoopDesk"; ValueData: """{app}\오토 매크로.exe"" --background"; Check: HasAutoStart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\오토 매크로.exe"; Description: "오토 매크로 실행"; Flags: nowait postinstall skipifsilent

[Code]
function HasAutoStart: Boolean;
begin
  Result := RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LoopDesk');
end;
