#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{5B142BDA-6A02-4D69-996E-67B770AA3F12}
AppName=HexaWall
AppVersion={#AppVersion}
AppPublisher=HexaWall contributors
DefaultDirName={localappdata}\Programs\HexaWall
DefaultGroupName=HexaWall
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts\installer
OutputBaseFilename=HexaWall-{#AppVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\hexawall.ico
UninstallDisplayIcon={app}\HexaWall.exe
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no
AppMutex=Local\HexaWall.5B142BDA-6A02-4D69-996E-67B770AA3F12
SetupLogging=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Iniciar HexaWall al entrar en Windows"; Flags: unchecked
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; Flags: unchecked

[Files]
Source: "{#PublishDir}\HexaWall.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\HexaWall"; Filename: "{app}\HexaWall.exe"
Name: "{autodesktop}\HexaWall"; Filename: "{app}\HexaWall.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HexaWall"; ValueData: """{app}\HexaWall.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\HexaWall.exe"; Description: "Abrir HexaWall"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'HexaWall');
end;

