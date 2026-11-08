; Kwanteks' NIC Tweaker — Inno Setup 6
; AppId is permanent — do not change after first release.
; No apostrophe in AppName — Inno [Code] Pascal strings break on '.
#define MyAppName "Kwanteks NIC Tweaker"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Kwanteks"
#define MyAppExeName "KwanteksNicTweaker.exe"
#define MyDataFolder "KwanteksNICTweaker"
#ifndef MyAppSource
  #define MyAppSource "..\publish\app"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\dist"
#endif

[Setup]
AppId={{F8528B97-5A80-4590-80E3-D007988C4FC6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=DISCLAIMER.txt
OutputDir={#MyOutputDir}
OutputBaseFilename=KwanteksNICTweaker-Setup-{#MyAppVersion}
SetupIconFile=..\Kwanteks' NIC Tweaker\Assets\Kwanteks.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}.0
; Optional code signing (uncomment and pass /DMySignTool=... from build.ps1 -Sign):
; SignTool=signtool
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Only publish\app (WinUI output). Repo scripts like St1icky's OG Network_Tweaker.ps1 are never included.
Source: "{#MyAppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.ps1"

[Dirs]
Name: "{commonappdata}\{#MyDataFolder}"; Permissions: users-modify
Name: "{commonappdata}\{#MyDataFolder}\RestorePoints"; Permissions: users-modify
Name: "{commonappdata}\{#MyDataFolder}\Exports"; Permissions: users-modify
Name: "{commonappdata}\{#MyDataFolder}\Presets"; Permissions: users-modify
Name: "{commonappdata}\{#MyDataFolder}\DriverBackup"; Permissions: users-modify
Name: "{commonappdata}\{#MyDataFolder}\Logs"; Permissions: users-modify
Name: "{commonappdata}\{#MyDataFolder}\Backups"; Permissions: users-modify

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[Code]
function IsUpgrade: Boolean;
begin
  Result := RegKeyExists(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1')
    or RegKeyExists(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1');
end;

function GetInstalledVersion: String;
var
  UninstallKey: String;
  Version: String;
begin
  Result := '';
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1';
  if RegQueryStringValue(HKLM64, UninstallKey, 'DisplayVersion', Version) then
    Result := Version
  else if RegQueryStringValue(HKLM, UninstallKey, 'DisplayVersion', Version) then
    Result := Version;
end;

function CompareVersion(V1, V2: String): Integer;
var
  P1, P2, N1, N2: Integer;
  S1, S2: String;
begin
  S1 := V1; S2 := V2;
  while (S1 <> '') or (S2 <> '') do
  begin
    P1 := Pos('.', S1); P2 := Pos('.', S2);
    if P1 = 0 then begin N1 := StrToIntDef(S1, 0); S1 := ''; end
    else begin N1 := StrToIntDef(Copy(S1, 1, P1 - 1), 0); Delete(S1, 1, P1); end;
    if P2 = 0 then begin N2 := StrToIntDef(S2, 0); S2 := ''; end
    else begin N2 := StrToIntDef(Copy(S2, 1, P2 - 1), 0); Delete(S2, 1, P2); end;
    if N1 < N2 then begin Result := -1; Exit; end;
    if N1 > N2 then begin Result := 1; Exit; end;
  end;
  Result := 0;
end;

function InitializeSetup: Boolean;
var
  Installed: String;
begin
  Result := True;

  if not IsWin64 then
  begin
    MsgBox('{#MyAppName} requires 64-bit Windows.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if GetWindowsVersion < $0A000000 then
  begin
    MsgBox('{#MyAppName} requires Windows 10 version 1809 (build 17763) or later.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Installed := GetInstalledVersion;
  if Installed <> '' then
  begin
    if CompareVersion('{#MyAppVersion}', Installed) < 0 then
    begin
      MsgBox('A newer version (' + Installed + ') is already installed. Downgrades are not supported. Uninstall it first if you need an older build.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  Keep: Boolean;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  DataDir := ExpandConstant('{commonappdata}\{#MyDataFolder}');
  if not DirExists(DataDir) then
    Exit;

  // Silent uninstall keeps user data by default.
  if UninstallSilent then
    Exit;

  Keep := MsgBox('Keep your restore points, exports and settings?' + #13#10 + #13#10 +
    'Recommended: Yes' + #13#10 + #13#10 +
    'Folder: ' + DataDir, mbConfirmation, MB_YESNO) = IDYES;

  if not Keep then
  begin
    DelTree(DataDir, True, True, True);
  end;
end;
