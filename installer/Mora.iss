; Mora's installer (Inno Setup 6). Built by release.ps1, which publishes the app first.
; Per user, no admin rights, into the same folder install.ps1 used (%LOCALAPPDATA%\Programs\Mora), so an
; earlier install is simply replaced. Uninstalling removes the app, its Start menu shortcut and its start-with-
; Windows entry; settings and shelf in %LOCALAPPDATA%\Mora are kept for a later reinstall.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{A77BAD25-1755-43E4-A014-067F9F806DD9}
AppName=Mora
AppVersion={#AppVersion}
AppVerName=Mora {#AppVersion}
AppPublisher=anderson3x11
AppPublisherURL=https://github.com/anderson3x11/mora
AppSupportURL=https://github.com/anderson3x11/mora/issues
AppUpdatesURL=https://github.com/anderson3x11/mora/releases
DefaultDirName={userpf}\Mora
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=Mora-Setup-{#AppVersion}
SetupIconFile=..\src\Mora\Mora.ico
UninstallDisplayIcon={app}\Mora.exe
UninstallDisplayName=Mora
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[InstallDelete]
; The folder is Mora's alone: start from empty, so an update or an earlier script install leaves nothing behind.
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Files]
Source: "..\artifacts\Mora\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Mora"; Filename: "{app}\Mora.exe"; Comment: "The island at the top of your screen"

[Run]
; Started through Explorer, like a Start menu launch, rather than as a child of the setup.
Filename: "{win}\explorer.exe"; Parameters: """{app}\Mora.exe"""; Description: "{cm:LaunchProgram,Mora}"; Flags: nowait postinstall skipifsilent

[Code]
// Mora sits in the tray: stop it before its files are replaced or removed.
procedure StopMora();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im Mora.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopMora();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopMora();
    // Written by Mora itself when "Start Mora when Windows starts" is on.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Mora');
  end;
end;
