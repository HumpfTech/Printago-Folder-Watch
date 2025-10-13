# Printago Folder Watch Installer
# Run this with PowerShell as Administrator

param(
    [string]$InstallPath = "$env:ProgramFiles\Printago Folder Watch"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Printago Folder Watch Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This installer must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click Install.ps1 and select 'Run with PowerShell as Administrator'" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}

# Check if Node.js is installed
Write-Host "Checking for Node.js..." -ForegroundColor Yellow
$nodeVersion = $null
try {
    $nodeVersion = node --version 2>$null
} catch {}

if (-not $nodeVersion) {
    Write-Host "ERROR: Node.js is not installed." -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Node.js first:" -ForegroundColor Yellow
    Write-Host "1. Download from: https://nodejs.org" -ForegroundColor White
    Write-Host "2. Run the installer (use all defaults)" -ForegroundColor White
    Write-Host "3. Restart this installer" -ForegroundColor White
    Write-Host ""
    $open = Read-Host "Open Node.js website now? (Y/N)"
    if ($open -eq "Y" -or $open -eq "y") {
        Start-Process "https://nodejs.org"
    }
    exit 1
}
Write-Host "✓ Node.js found: $nodeVersion" -ForegroundColor Green

# Ask for install location
Write-Host ""
Write-Host "Install Location:" -ForegroundColor Cyan
Write-Host "Default: $InstallPath" -ForegroundColor White
$customPath = Read-Host "Press Enter for default, or type a custom path"
if ($customPath) {
    $InstallPath = $customPath
}

# Check if already installed
$alreadyInstalled = Test-Path $InstallPath
if ($alreadyInstalled) {
    Write-Host ""
    Write-Host "WARNING: Installation detected at $InstallPath" -ForegroundColor Yellow
    $reinstall = Read-Host "Reinstall/Upgrade? (Y/N)"
    if ($reinstall -ne "Y" -and $reinstall -ne "y") {
        Write-Host "Installation cancelled." -ForegroundColor Red
        exit 0
    }

    # Stop any running instances
    Write-Host "Stopping existing instances..." -ForegroundColor Yellow
    Stop-Process -Name "node" -Force -ErrorAction SilentlyContinue
    Get-Process | Where-Object {$_.MainWindowTitle -like "*Printago*"} | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# Create install directory
Write-Host ""
Write-Host "Installing to: $InstallPath" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null

# Copy files
Write-Host "Copying files..." -ForegroundColor Yellow
$sourceDir = $PSScriptRoot
Copy-Item "$sourceDir\PrintagoFolderWatch.vbs" -Destination $InstallPath -Force
Copy-Item "$sourceDir\RunTray.vbs" -Destination $InstallPath -Force
Copy-Item "$sourceDir\tray.ps1" -Destination $InstallPath -Force
Copy-Item "$sourceDir\app-auto.js" -Destination $InstallPath -Force
Copy-Item "$sourceDir\package.json" -Destination $InstallPath -Force

# Install npm dependencies
Write-Host "Installing dependencies (this may take a minute)..." -ForegroundColor Yellow
Push-Location $InstallPath
npm install --production 2>&1 | Out-Null
Pop-Location

if (-not (Test-Path "$InstallPath\node_modules")) {
    Write-Host "ERROR: Failed to install dependencies" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Dependencies installed" -ForegroundColor Green

# Create Start Menu shortcut
Write-Host "Creating shortcuts..." -ForegroundColor Yellow
$startMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs"
$WScriptShell = New-Object -ComObject WScript.Shell
$shortcut = $WScriptShell.CreateShortcut("$startMenuPath\Printago Folder Watch.lnk")
$shortcut.TargetPath = "wscript.exe"
$shortcut.Arguments = "`"$InstallPath\PrintagoFolderWatch.vbs`""
$shortcut.WorkingDirectory = $InstallPath
$shortcut.Description = "Printago Folder Watch - Auto upload files to cloud"
$shortcut.Save()

# Ask for desktop shortcut
$desktop = Read-Host "Create desktop shortcut? (Y/N)"
if ($desktop -eq "Y" -or $desktop -eq "y") {
    $desktopShortcut = $WScriptShell.CreateShortcut("$env:USERPROFILE\Desktop\Printago Folder Watch.lnk")
    $desktopShortcut.TargetPath = "wscript.exe"
    $desktopShortcut.Arguments = "`"$InstallPath\PrintagoFolderWatch.vbs`""
    $desktopShortcut.WorkingDirectory = $InstallPath
    $desktopShortcut.Description = "Printago Folder Watch"
    $desktopShortcut.Save()
    Write-Host "✓ Desktop shortcut created" -ForegroundColor Green
}

# Ask for startup
$startup = Read-Host "Start automatically when Windows starts? (Y/N)"
if ($startup -eq "Y" -or $startup -eq "y") {
    $startupShortcut = $WScriptShell.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\Printago Folder Watch.lnk")
    $startupShortcut.TargetPath = "wscript.exe"
    $startupShortcut.Arguments = "`"$InstallPath\PrintagoFolderWatch.vbs`""
    $startupShortcut.WorkingDirectory = $InstallPath
    $startupShortcut.Description = "Printago Folder Watch"
    $startupShortcut.Save()
    Write-Host "✓ Auto-start enabled" -ForegroundColor Green
}

# Create uninstaller
$uninstallScript = @"
# Printago Folder Watch Uninstaller
Write-Host "Uninstalling Printago Folder Watch..." -ForegroundColor Yellow

# Stop processes
Stop-Process -Name "node" -Force -ErrorAction SilentlyContinue
Get-Process | Where-Object {`$_.MainWindowTitle -like "*Printago*"} | Stop-Process -Force -ErrorAction SilentlyContinue

# Remove files
Remove-Item "$InstallPath" -Recurse -Force -ErrorAction SilentlyContinue

# Remove shortcuts
Remove-Item "$startMenuPath\Printago Folder Watch.lnk" -Force -ErrorAction SilentlyContinue
Remove-Item "`$env:USERPROFILE\Desktop\Printago Folder Watch.lnk" -Force -ErrorAction SilentlyContinue
Remove-Item "`$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\Printago Folder Watch.lnk" -Force -ErrorAction SilentlyContinue

Write-Host "✓ Uninstall complete" -ForegroundColor Green
Read-Host "Press Enter to close"
"@

$uninstallScript | Out-File -FilePath "$InstallPath\Uninstall.ps1" -Encoding UTF8

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Installation Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "To start: Look for 'Printago Folder Watch' in your Start Menu" -ForegroundColor White
Write-Host "To uninstall: Run Uninstall.ps1 in $InstallPath" -ForegroundColor White
Write-Host ""

$launch = Read-Host "Launch Printago Folder Watch now? (Y/N)"
if ($launch -eq "Y" -or $launch -eq "y") {
    Start-Process "wscript.exe" -ArgumentList "`"$InstallPath\PrintagoFolderWatch.vbs`""
    Write-Host "✓ Application started - check your system tray!" -ForegroundColor Green
}

Write-Host ""
Read-Host "Press Enter to close"
