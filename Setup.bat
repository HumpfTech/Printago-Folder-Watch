@echo off
setlocal enabledelayedexpansion

:: Check for admin rights
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo ================================================
    echo   ERROR: Administrator privileges required
    echo ================================================
    echo.
    echo Please right-click this file and select
    echo "Run as administrator"
    echo.
    pause
    exit /b 1
)

cls
echo ================================================
echo   Printago Folder Watch Installer
echo ================================================
echo.

:: Check if .NET 8.0 or higher is installed
echo Checking for .NET Runtime...
dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App 8." >nul 2>&1
if %errorLevel% equ 0 goto :dotnet_found

dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App 9." >nul 2>&1
if %errorLevel% equ 0 goto :dotnet_found

echo .NET Desktop Runtime not found!
echo.
echo Downloading .NET 8.0 Desktop Runtime...
echo This may take a few minutes...
echo.

:: Download .NET 8.0 Desktop Runtime installer
powershell -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://download.visualstudio.microsoft.com/download/pr/907765b0-2bf8-494e-93aa-5ef9553c5d68/a9308dc010617e6716c0e6abd53b05ce/windowsdesktop-runtime-8.0.8-win-x64.exe' -OutFile '%TEMP%\dotnet-runtime-installer.exe'}"

if not exist "%TEMP%\dotnet-runtime-installer.exe" (
    echo ERROR: Failed to download .NET Runtime installer
    echo.
    echo Please manually download and install from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo Installing .NET Desktop Runtime...
"%TEMP%\dotnet-runtime-installer.exe" /install /quiet /norestart

:: Wait for installation
timeout /t 5 /nobreak >nul

:: Clean up
del "%TEMP%\dotnet-runtime-installer.exe" >nul 2>&1

echo.
echo .NET Desktop Runtime installed successfully!
echo.

:dotnet_found
echo [OK] .NET Desktop Runtime found
echo.

:: Ask for install location
set "INSTALL_DIR=%ProgramFiles%\Printago Folder Watch"
echo Default install location: %INSTALL_DIR%
echo.
set /p "CUSTOM_DIR=Press ENTER to use default, or type custom path: "
if not "!CUSTOM_DIR!"=="" set "INSTALL_DIR=!CUSTOM_DIR!"

:: Check if already installed
if exist "%INSTALL_DIR%\PrintagoFolderWatch.exe" (
    echo.
    echo ================================================
    echo   Existing Installation Detected
    echo ================================================
    echo.
    echo Printago Folder Watch is already installed at:
    echo %INSTALL_DIR%
    echo.
    choice /C YN /M "Do you want to upgrade/reinstall"
    if errorlevel 2 (
        echo.
        echo Installation cancelled.
        pause
        exit /b 0
    )

    echo.
    echo Stopping existing instances...
    taskkill /F /IM PrintagoFolderWatch.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
)

:: Create install directory
echo.
echo Installing to: %INSTALL_DIR%
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

:: Copy files
echo Copying files...
xcopy /Y /Q "%~dp0bin\Release\net9.0-windows\publish\*.*" "%INSTALL_DIR%\" >nul 2>&1
if errorlevel 1 (
    :: Fallback to current directory if publish doesn't exist
    xcopy /Y /Q "%~dp0PrintagoFolderWatch.exe" "%INSTALL_DIR%\" >nul 2>&1
    xcopy /Y /Q "%~dp0*.dll" "%INSTALL_DIR%\" >nul 2>&1
)

:: Create Start Menu shortcut
echo Creating shortcuts...
powershell -Command "$WS = New-Object -ComObject WScript.Shell; $SC = $WS.CreateShortcut('%ProgramData%\Microsoft\Windows\Start Menu\Programs\Printago Folder Watch.lnk'); $SC.TargetPath = '%INSTALL_DIR%\PrintagoFolderWatch.exe'; $SC.WorkingDirectory = '%INSTALL_DIR%'; $SC.Description = 'Printago Folder Watch'; $SC.Save()"

:: Ask for desktop shortcut
choice /C YN /M "Create desktop shortcut"
if not errorlevel 2 (
    powershell -Command "$WS = New-Object -ComObject WScript.Shell; $SC = $WS.CreateShortcut('%USERPROFILE%\Desktop\Printago Folder Watch.lnk'); $SC.TargetPath = '%INSTALL_DIR%\PrintagoFolderWatch.exe'; $SC.WorkingDirectory = '%INSTALL_DIR%'; $SC.Description = 'Printago Folder Watch'; $SC.Save()"
    echo [OK] Desktop shortcut created
)

:: Ask for auto-start
echo.
choice /C YN /M "Start automatically when Windows starts"
if not errorlevel 2 (
    powershell -Command "$WS = New-Object -ComObject WScript.Shell; $SC = $WS.CreateShortcut('%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Printago Folder Watch.lnk'); $SC.TargetPath = '%INSTALL_DIR%\PrintagoFolderWatch.exe'; $SC.WorkingDirectory = '%INSTALL_DIR%'; $SC.Description = 'Printago Folder Watch'; $SC.Save()"
    echo [OK] Auto-start enabled
)

:: Create uninstaller
echo Creating uninstaller...
(
echo @echo off
echo echo Uninstalling Printago Folder Watch...
echo taskkill /F /IM PrintagoFolderWatch.exe ^>nul 2^>^&1
echo timeout /t 2 /nobreak ^>nul
echo rmdir /S /Q "%INSTALL_DIR%"
echo del "%ProgramData%\Microsoft\Windows\Start Menu\Programs\Printago Folder Watch.lnk" ^>nul 2^>^&1
echo del "%USERPROFILE%\Desktop\Printago Folder Watch.lnk" ^>nul 2^>^&1
echo del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Printago Folder Watch.lnk" ^>nul 2^>^&1
echo echo Uninstall complete!
echo pause
) > "%INSTALL_DIR%\Uninstall.bat"

echo.
echo ================================================
echo   Installation Complete!
echo ================================================
echo.
echo Printago Folder Watch has been installed to:
echo %INSTALL_DIR%
echo.
echo To start: Look for 'Printago Folder Watch' in Start Menu
echo           or check your system tray
echo.
echo To uninstall: Run Uninstall.bat in the install folder
echo.

choice /C YN /M "Launch Printago Folder Watch now"
if not errorlevel 2 (
    start "" "%INSTALL_DIR%\PrintagoFolderWatch.exe"
    echo.
    echo Application started - check your system tray!
)

echo.
pause
