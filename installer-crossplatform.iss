; Printago Folder Watch Cross-Platform Installer
; Inno Setup Script for Avalonia Application

#define MyAppName "Printago Folder Watch"
#define MyAppVersion "2.9.2"
#define MyAppPublisher "Humpf Tech LLC"
#define MyAppExeName "PrintagoFolderWatch.exe"

[Setup]
AppId={{8F4C3D2E-9B7A-4F1C-8E3D-5A6B7C8D9E0F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=dist
OutputBaseFilename=PrintagoFolderWatch-CrossPlatform-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
VersionInfoVersion={#MyAppVersion}
UsePreviousAppDir=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start automatically when Windows starts"; GroupDescription: "Startup:"

[Files]
; Self-contained cross-platform build
Source: "dist\cross-platform-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall shellexec skipifsilent

[Code]
// Kill any running instances of the app
procedure KillRunningInstances();
var
  ResultCode: Integer;
  Retries: Integer;
begin
  // Try to kill the app multiple times to ensure it's stopped
  for Retries := 1 to 3 do
  begin
    Exec('taskkill', '/F /IM PrintagoFolderWatch.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
  // Final wait to ensure processes are fully terminated
  Sleep(1000);
end;

function InitializeSetup(): Boolean;
var
  Version: String;
begin
  Result := True;

  // Always kill running instances first - this ensures clean upgrade
  KillRunningInstances();

  // Check if already installed
  if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F4C3D2E-9B7A-4F1C-8E3D-5A6B7C8D9E0F}_is1', 'DisplayVersion', Version) then
  begin
    if Version = '{#MyAppVersion}' then
    begin
      if MsgBox('Printago Folder Watch version ' + Version + ' is already installed.' + #13#10 + #13#10 +
                'Do you want to reinstall?', mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end
    else
    begin
      if MsgBox('Printago Folder Watch version ' + Version + ' is currently installed.' + #13#10 + #13#10 +
                'Do you want to upgrade to version {#MyAppVersion}?' + #13#10 + #13#10 +
                'Your settings will be preserved.', mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;
end;

// This runs right before files are installed - kill the app again to be sure
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  KillRunningInstances();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // Kill before uninstall starts
    KillRunningInstances();
  end;
end;
