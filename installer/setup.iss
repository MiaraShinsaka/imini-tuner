; iMini Tuner - Inno Setup 6 script
; 编译：ISCC.exe setup.iss

#define MyAppName "iMini Tuner"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "iMini Tuner contributors"
#define MyAppExeName "iMini Tuner.exe"
#define MyAppId "{{C23E1CF6-CAF0-496E-996F-9A81A079CB93}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\iMini Tuner
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#SourcePath}\..\dist
OutputBaseFilename=iMiniTuner-Setup
SetupIconFile={#SourcePath}\..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
UsePreviousAppDir=no
SetupLogging=yes
AppMutex=Local\iminiCC.ControlCenter
VersionInfoVersion=2.2.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
LicenseFile={#SourcePath}\..\LICENSE

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#SourcePath}\Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourcePath}\..\dist\iMini Tuner.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\show-window.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\show-window.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "README-installed.md"; DestDir: "{app}"; DestName: "README.md"; Flags: ignoreversion
Source: "{#SourcePath}\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{autoprograms}\iMini Tuner（显示主界面）"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--show"
[Run]
; The app requires administrator. "runascurrentuser" launches it with Setup's
; elevated token; without it Inno uses the original (pre-elevation) user token
; and CreateProcess fails with error 5 / 740 after a normal double-click.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM ""iMini Tuner.exe"""; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""iMini Tuner"" /F"; Flags: runhidden; RunOnceId: "DeleteAutoStart"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""iMini Tuner"" /F"; Flags: runhidden; RunOnceId: "DeleteLegacyAutoStart"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\iminiControlCenter"
Type: filesandordirs; Name: "{commonappdata}\iMiniTuner"
Type: filesandordirs; Name: "{app}"

[InstallDelete]
Type: files; Name: "{app}\uninstall.exe"
Type: files; Name: "{app}\显示主界面.ps1"
Type: files; Name: "{app}\显示主界面.cmd"

[Code]
function InitializeSetup(): Boolean;
var
  OldUninstaller: String;
  ResultCode: Integer;
begin
  { Remove the pre-2.1 “iMini Tuner” installation from the default path. }
  OldUninstaller := ExpandConstant('{autopf}\iMini Tuner\unins000.exe');
  if FileExists(OldUninstaller) then
  begin
    Exec(OldUninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  Result := True;
end;

