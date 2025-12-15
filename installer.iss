; Printago Folder Watch Installer
; Inno Setup Script for .NET Application

#define MyAppName "Printago Folder Watch"
#define MyAppVersion "2.6"
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
OutputBaseFilename=PrintagoFolderWatch-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
VersionInfoVersion={#MyAppVersion}
; Upgrade settings - allow installing over existing version
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
; Main application files from Release build (new src structure)
Source: "src\PrintagoFolderWatch.Windows\bin\Release\net9.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Exclude unnecessary files
; Note: SQLite native libraries are included in the bin output

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall shellexec skipifsilent

[Code]
function CheckDotNetInstalled(): Boolean;
var
  FoundVersion: String;
begin
  Result := False;
  FoundVersion := '';

  // Check for .NET 9.0 WindowsDesktop runtime (required for Windows Forms apps)
  if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.0') then
  begin
    Result := True;
    FoundVersion := '9.0.0';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.1') then
  begin
    Result := True;
    FoundVersion := '9.0.1';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.2') then
  begin
    Result := True;
    FoundVersion := '9.0.2';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.3') then
  begin
    Result := True;
    FoundVersion := '9.0.3';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.4') then
  begin
    Result := True;
    FoundVersion := '9.0.4';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.5') then
  begin
    Result := True;
    FoundVersion := '9.0.5';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.6') then
  begin
    Result := True;
    FoundVersion := '9.0.6';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.7') then
  begin
    Result := True;
    FoundVersion := '9.0.7';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.8') then
  begin
    Result := True;
    FoundVersion := '9.0.8';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.9') then
  begin
    Result := True;
    FoundVersion := '9.0.9';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.10') then
  begin
    Result := True;
    FoundVersion := '9.0.10';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.11') then
  begin
    Result := True;
    FoundVersion := '9.0.11';
  end
  else if DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.12') then
  begin
    Result := True;
    FoundVersion := '9.0.12';
  end;

  // Show debug message with result
  if Result then
  begin
    MsgBox('.NET 9.0 WindowsDesktop Runtime DETECTED!' + #13#10 + #13#10 +
           'Version found: ' + FoundVersion + #13#10 +
           'Path: C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\' + FoundVersion,
           mbInformation, MB_OK);
  end
  else
  begin
    MsgBox('.NET 9.0 WindowsDesktop Runtime NOT DETECTED!' + #13#10 + #13#10 +
           'Checked path: C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.x',
           mbError, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
var
  Version: String;
  DotNetInstalled: Boolean;
  ResultCode: Integer;
  IsUpgrade: Boolean;
begin
  Result := True;
  IsUpgrade := False;

  // Check if already installed
  if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F4C3D2E-9B7A-4F1C-8E3D-5A6B7C8D9E0F}_is1', 'DisplayVersion', Version) then
  begin
    IsUpgrade := True;

    // Compare versions
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

    // Close running instance before upgrade
    Exec('taskkill', '/F /IM PrintagoFolderWatch.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Give it a moment to fully close
    Sleep(1000);
  end;

  // Check if .NET 9.0 Desktop Runtime is installed (with debug info)
  DotNetInstalled := CheckDotNetInstalled();

  if not DotNetInstalled then
  begin
    if MsgBox('.NET 9.0 Desktop Runtime is required but not detected.' + #13#10 + #13#10 +
             'Would you like to download it now?' + #13#10 +
             '(You will need to install it before running Printago Folder Watch)', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/9.0', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;

    MsgBox('Please install .NET 9.0 Desktop Runtime and run this installer again.', mbInformation, MB_OK);
    Result := False;
    Exit;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Kill any running instances
    Exec('taskkill', '/F /IM PrintagoFolderWatch.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
