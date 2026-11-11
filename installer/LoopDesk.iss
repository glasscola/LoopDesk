#ifndef AppBinary
  #define AppBinary "..\downloads\LoopDesk.exe"
#endif
#ifndef GuideFile
  #define GuideFile "..\docs\LoopDesk-사용설명서.pdf"
#endif
[Setup]
AppId={{D7F5141A-5F53-4F24-B423-938B07F92AB0}
AppName=LoopDesk
AppVersion=1.1.0
AppPublisher=glasscola
AppPublisherURL=https://github.com/glasscola/LoopDesk
AppSupportURL=https://github.com/glasscola/LoopDesk/issues
AppUpdatesURL=https://github.com/glasscola/LoopDesk/releases
DefaultDirName={localappdata}\Programs\LoopDesk
DefaultGroupName=LoopDesk
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\outputs
OutputBaseFilename=LoopDesk-Setup-1.1.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\LoopDesk.exe
AppMutex=Local\LoopDesk.Main
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로가기 만들기"; Flags: unchecked

[Files]
Source: "{#AppBinary}"; DestDir: "{app}"; DestName: "LoopDesk.exe"; Flags: ignoreversion
Source: "{#GuideFile}"; DestDir: "{app}"; DestName: "LoopDesk-사용설명서.pdf"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\LoopDesk"; Filename: "{app}\LoopDesk.exe"
Name: "{autodesktop}\LoopDesk"; Filename: "{app}\LoopDesk.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LoopDesk"; ValueData: """{app}\LoopDesk.exe"" --background"; Check: HasAutoStart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LoopDesk.exe"; Description: "LoopDesk 실행"; Flags: nowait postinstall skipifsilent

[Code]
function HasAutoStart: Boolean;
begin
  Result := RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LoopDesk');
end;
