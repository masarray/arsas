#define MyAppName "ArIED 61850"
#define MyAppVersion "1.6.5"
#define MyAppPublisher "Mas Ari / masarray"
#define MyAppExeName "ArIED61850.exe"

[Setup]
AppId={{D8C6E497-6185-4F80-A21D-618500000001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ArIED 61850
DefaultGroupName={#MyAppName}
OutputDir=output
OutputBaseFilename=ArIED61850-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\app-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\dist\ArIED61850-{#MyAppVersion}-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
