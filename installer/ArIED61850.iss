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

#ifdef SmvPreviewBuild
  ; Prevent a preview based on an earlier source version from replacing stable ARSAS.
  ; Independent AppId, installation directory, Start Menu entry and uninstaller.
  #define AppName "ARSAS SMV Engineering Preview"
  #define AppDirectory "ARSAS-SMV-Preview"
  #define AppShortcut "ARSAS SMV Preview"
#else
  #define AppName "ARSAS - Smart IEC 61850 Communication Tester"
  #define AppDirectory "ARSAS"
  #define AppShortcut "ARSAS"
#endif
#define AppExeName "ARSAS.exe"
#define AppPublisher "Ari Sulistiono / masarray"
#define AppUrl "https://github.com/masarray/arsas"

[Setup]
#ifdef SmvPreviewBuild
AppId={{9E3B17A4-38EC-4B2B-B61A-72788FF78B8A}
#else
AppId={{B68E7E86-4881-40CA-9BE2-5C553F74C72F}
#endif
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
DefaultDirName={autopf}\{#AppDirectory}
DefaultGroupName={#AppShortcut}
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
; Preview installs into Program Files and must never silently fall back to
; a current-user token when upgrading an existing protected installation.
#ifndef SmvPreviewBuild
PrivilegesRequiredOverridesAllowed=dialog
#endif
; Let Restart Manager close applications cleanly before any file replacement.
; Do not force-kill arbitrary processes with unsaved engineering work.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll,*.chm
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
; Dedicated shell icon is embedded separately from the .NET single-file host.
; Keep this exact P0 icon contract intact for stable AND preview installations.
Source: "..\Assets\app-icon.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

#ifdef SmvPreviewBuild
; A still-loaded .NET framework/private DLL must be queued for replacement
; on Windows restart rather than presenting Retry / Skip / Cancel mid-upgrade.
; This requires elevation and must never be used for shared system DLLs.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace
#else
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{autoprograms}\{#AppShortcut}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\app-icon.ico"; AppUserModelID: "masarray.ARSAS.IEC61850"
Name: "{autodesktop}\{#AppShortcut}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\app-icon.ico"; AppUserModelID: "masarray.ARSAS.IEC61850"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch ARSAS"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
#ifdef SmvPreviewBuild
procedure RegisterExtraCloseApplicationsResources;
var
  LegacyDll: String;
begin
  { Versions built with older .NET publish settings may leave Accessibility.dll
    in the installation folder even when the newest published payload omits it.
    Include that legacy file in Restart Manager's graceful close check. }
  LegacyDll := ExpandConstant('{app}\Accessibility.dll');
  if FileExists(LegacyDll) then
    RegisterExtraCloseApplicationsResource(False, LegacyDll);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProbePath: String;
begin
  Result := '';
  { Check protected/upgraded folders BEFORE writing hundreds of files. }
  if not DirExists(ExpandConstant('{app}')) then
    Exit;

  ProbePath := ExpandConstant('{app}\.arsas-installer-access-check.tmp');
  if not SaveStringToFile(ProbePath, 'ARSAS installation access check', False) then
  begin
    Result :=
      'Windows cannot update the existing ARSAS SMV Preview folder.' + #13#10 +
      'The installer needs administrator access to that folder.' + #13#10 +
      'No application files were replaced. Please ask your computer administrator to check access permissions.';
    Log('SMART_UPGRADE_PREFLIGHT: failed write probe in ' + ExpandConstant('{app}'));
    Exit;
  end;
  if not DeleteFile(ProbePath) then
  begin
    Result := 'Windows cannot update the existing ARSAS SMV Preview folder. ' +
      'A protected file prevented the pre-installation access check.';
    Log('SMART_UPGRADE_PREFLIGHT: failed to remove write probe');
    Exit;
  end;
  Log('SMART_UPGRADE_PREFLIGHT: existing installation folder is writable');
end;
#endif

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
      'Npcap was not detected. Install Npcap separately before using live GOOSE/SMV capture or the Clock Sync RAW fallback that keeps SNTP working when Windows already owns UDP/123. ' +
      'Keep WinPcap API compatibility enabled when required by your engineering workstation policy.',
      mbInformation,
      MB_OK,
      IDOK);
  end;
end;
