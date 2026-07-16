; ARSAS Windows installer
; Values are injected by scripts/build-windows-installer.ps1 through ISCC /D switches.

#ifndef AppVersion
  #define AppVersion "1.6.16"
#endif
#ifndef AppVersionNumeric
  #define AppVersionNumeric "1.6.16.0"
#endif
#ifndef SourceDir
  #error SourceDir must point to the published Windows application folder.
#endif
#ifndef OutputDir
  #define OutputDir ".\dist"
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "ARSAS-setup"
#endif

#define AppName "ARSAS - Smart IEC 61850 Communication Tester"
#define AppExeName "ARSAS.exe"
#define AppPublisher "Ari Sulistiono / masarray"
#define AppUrl "https://github.com/masarray/ArIED61850Tester"

[Setup]
AppId={{B68E7E86-4881-40CA-9BE2-5C553F74C72F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductVersion={#AppVersionNumeric}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Windows Installer
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\ARSAS
DefaultGroupName=ARSAS
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\Assets\app-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardResizable=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
SetupLogging=yes
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ARSAS"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\ARSAS"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch ARSAS"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
function IsNpcapInstalled: Boolean;
begin
  Result :=
    RegKeyExists(HKLM64, 'SOFTWARE\Npcap') or
    RegKeyExists(HKLM32, 'SOFTWARE\Npcap') or
    FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) or
    FileExists(ExpandConstant('{syswow64}\Npcap\wpcap.dll'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not WizardSilent) and (not IsNpcapInstalled) then
  begin
    SuppressibleMsgBox(
      'ARSAS is installed and its MMS/SCL features are ready.' + #13#10 + #13#10 +
      'Npcap was not detected. Install Npcap separately before using the GOOSE Subscriber. ' +
      'Keep WinPcap API compatibility enabled when required by your engineering workstation policy.',
      mbInformation,
      MB_OK,
      IDOK);
  end;
end;
