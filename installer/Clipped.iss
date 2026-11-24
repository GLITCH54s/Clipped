; Classic (non-store) Windows installer for Clipped.
; Build: bash installer/build.sh   (publishes the app, then compiles this script)

#define AppName "Clipped"
#define AppVersion "0.1.0"
#define AppPublisher "Clipped contributors"
#define AppUrl "https://github.com/GLITCH54s/clipped"
#define AppExeName "Clipped.exe"

#ifndef PublishDir
  #define PublishDir "..\..\build\clipped-installer\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\build\clipped-installer\dist"
#endif

[Setup]
AppId=Clipped
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={localappdata}\Programs\Clipped
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Clipped-Setup-{#AppVersion}
SetupIconFile=..\assets\Clipped.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} setup
VersionInfoProductName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Launch {#AppName} when I sign in to Windows"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Clipped"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Parameters: "--picker"; Comment: "Open the Clipped clipboard picker"
Name: "{group}\Dashboard"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Parameters: "--dashboard"
Name: "{group}\Settings"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Parameters: "--settings"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Clipped"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Parameters: "--picker"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\clipped"; ValueType: string; ValueName: ""; ValueData: "URL:Clipped"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\clipped"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\clipped"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Clipped link"
Root: HKCU; Subkey: "Software\Classes\clipped\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\clipped\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName}"; Parameters: "--picker"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C powershell -NoProfile -ExecutionPolicy Bypass -Command ""Get-Process -Name Clipped -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -eq '{app}\{#AppExeName}' }} | Stop-Process -Force; Start-Sleep -Milliseconds 700"" > ""{tmp}\stop-clipped.log"" 2>&1"; Flags: runhidden; RunOnceId: "StopClipped"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Microsoft\Windows\Start Menu\Programs\Clipped"

[Code]
// Stops only the copy installed in {app} - other Clipped instances are left alone.
// PowerShell is launched through cmd: calling powershell.exe straight from Exec
// exits with code 2 before the script runs.
procedure PrepareInstall;
var
  Command: String;
  ResultCode: Integer;
begin
  Command := '/C powershell -NoProfile -ExecutionPolicy Bypass -Command "'
    + 'Get-Process -Name Clipped -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq ''' +
    ExpandConstant('{app}\{#AppExeName}') + ''' } | Stop-Process -Force; '
    + 'Start-Sleep -Milliseconds 700" > "' + ExpandConstant('{tmp}\prepare-install.log') + '" 2>&1';
  Exec(ExpandConstant('{cmd}'), Command, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('prepare install exit code: ' + IntToStr(ResultCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    PrepareInstall;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    MsgBox('{#AppName} was removed from this computer.' + #13#10 + #13#10 +
      'Your library and clipboard history are kept in:' + #13#10 +
      ExpandConstant('{userappdata}\Clipped'), mbInformation, MB_OK);
end;
