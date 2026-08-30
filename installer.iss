; Osage Lagtrain Wallpaper - Inno Setup script
; References: Lively src/installer/Script.iss L18-22 AppId, L58-60 Run uninsdeletevalue, L138-151 DelTree, L163-213 UnInstallOldVersion
; Must NOT use MSIX/Squirrel, HKLM, ProgramData, System32 dll

#define MyAppName "Osage Lagtrain Wallpaper"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "OsageLagtrain"
#define MyAppExeName "OsageLagtrain.exe"
#define MyAppId "{{OSAGE-LAG-001}}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/osage-lagtrain/wallpaper
DefaultDirName={localappdata}\OsageLagtrain
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=.
OutputBaseFilename=OsageLagtrain-Setup
SetupIconFile=src\App\Assets\StoreLogo.png
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
CloseApplications=no
RestartApplications=no
; No ProgramData, no HKLM - per-user only

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\OsageLagtrain.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "cycles\_template\*"; DestDir: "{app}\cycles\_template"; Flags: ignoreversion recursesubdirs
Source: "cycles\README.md"; DestDir: "{app}\cycles"; Flags: ignoreversion
Source: "docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs
Source: "CREDITS.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-modify
; Note: no System32 dll, no ProgramData

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Per-user autostart - HKCU only, uninsdeletevalue ensures clean uninstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OsageLagtrain"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Inno handles Registry uninsdeletevalue above; DelTree for user data is in [Code] with user prompt

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  V: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    V := MsgBox('Delete user data (settings/history/cycles)?' + #13#10 + 'This will remove %APPDATA%\OsageLagtrain and %LOCALAPPDATA%\OsageLagtrain.', mbConfirmation, MB_YESNO);
    if V = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\OsageLagtrain'), True, True, True);
      DelTree(ExpandConstant('{userappdata}\OsageLagtrain'), True, True, True);
      { {userappdata} = {appdata} roaming. Also handle explicit {appdata} for clarity }
      DelTree(ExpandConstant('{appdata}\OsageLagtrain'), True, True, True);
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;
