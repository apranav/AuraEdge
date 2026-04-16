; =============================================================================
;  AuraEdge – Inno Setup Script
;  Generates: AuraEdge_Setup.exe
;  Author:    Krishna Pranav
; =============================================================================

#define MyAppName      "AuraEdge"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "Krishna Pranav"
#define MyAppExeName   "AuraEdge.exe"
#define MyAppIconName  "AppIcon.ico"

; Path to the .NET publish / build output folder (relative to this .iss file)
#define BuildDir "..\EdgeLightApp\bin\Debug\net10.0-windows"

; Path to the project root where AppIcon.ico lives
#define ProjectDir "..\EdgeLightApp"

; =============================================================================
[Setup]
AppId={{F3A2C1D4-8E5B-4F7A-9C2D-1B3E4F6A7C8D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/krishnapranav
AppSupportURL=https://github.com/krishnapranav
AppUpdatesURL=https://github.com/krishnapranav

; Install directory
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; License
LicenseFile=license.txt

; Output
OutputDir=Output
OutputBaseFilename=AuraEdge_Setup
Compression=lzma2/ultra64
SolidCompression=yes

; Visuals – modern wizard with AuraEdge icon
WizardStyle=modern
WizardResizable=yes
SetupIconFile={#ProjectDir}\{#MyAppIconName}

; Require admin so we can write to Program Files
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Uninstall entry in Add/Remove Programs
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppIconName}

; Windows 10 minimum (SetWindowDisplayAffinity WDA_EXCLUDEFROMCAPTURE needs Win10)
MinVersion=10.0

; =============================================================================
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; =============================================================================
[Tasks]
; Desktop shortcut – optional checkbox shown to user
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; =============================================================================
[Files]
; ── Main executable & runtime files ─────────────────────────────────────────
Source: "{#BuildDir}\AuraEdge.exe";               DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\AuraEdge.dll";               DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\AuraEdge.deps.json";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\AuraEdge.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\System.Management.dll";      DestDir: "{app}"; Flags: ignoreversion

; ── App icon (used by shortcuts & Add/Remove Programs) ───────────────────────
Source: "{#ProjectDir}\{#MyAppIconName}";         DestDir: "{app}"; Flags: ignoreversion

; ── Runtimes folder (recursive) ─────────────────────────────────────────────
Source: "{#BuildDir}\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs

; =============================================================================
[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}";           Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconName}"; Comment: "Ambient edge lighting overlay"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

; Desktop shortcut (only if task selected)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconName}"; Tasks: desktopicon

; =============================================================================
[Run]
; Finish page – "Launch AuraEdge" checkbox
Filename: "{app}\{#MyAppExeName}"; \
  Description: "Launch {#MyAppName}"; \
  Flags: nowait postinstall skipifsilent

; =============================================================================
[UninstallDelete]
; Remove any leftover files the uninstaller wouldn't catch automatically
; NOTE: We deliberately do NOT remove %AppData%\AuraEdge (user settings / config)
Type: filesandordirs; Name: "{app}"

; =============================================================================
[Code]
// ---------------------------------------------------------------------------
// Prevent installing on top of a running AuraEdge process
// ---------------------------------------------------------------------------
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // If AuraEdge.exe is running, ask user to close it first
  if CheckForMutexes('AuraEdgeMutex') then
  begin
    MsgBox(
      'AuraEdge is currently running.' + #13#10 +
      'Please close it from the system tray before continuing.',
      mbInformation, MB_OK);
    Result := False;
  end;
end;
