# Build Printago Folder Watch Installer
# This script builds the application and creates an installer using Inno Setup

Write-Host "===== Printago Folder Watch Installer Build Script =====" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check for Inno Setup
Write-Host "[1/5] Checking for Inno Setup..." -ForegroundColor Yellow
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\iscc.exe",
    "C:\Program Files\Inno Setup 6\iscc.exe",
    "C:\Program Files (x86)\Inno Setup 5\iscc.exe",
    "C:\Program Files\Inno Setup 5\iscc.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = $path
        Write-Host "  Found Inno Setup at: $path" -ForegroundColor Green
        break
    }
}

if (-not $iscc) {
    Write-Host "  ERROR: Inno Setup not found!" -ForegroundColor Red
    Write-Host "  Please download and install from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    exit 1
}

# Step 2: Check for icon file (optional but recommended)
Write-Host "[2/5] Checking for icon file..." -ForegroundColor Yellow
if (Test-Path "icon.ico") {
    Write-Host "  Found icon.ico" -ForegroundColor Green
} else {
    Write-Host "  WARNING: icon.ico not found - installer will use default icon" -ForegroundColor Yellow
}

# Step 3: Build the .NET application
Write-Host "[3/5] Building .NET application..." -ForegroundColor Yellow
$buildResult = dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Build successful!" -ForegroundColor Green

# Step 4: Check if dist directory exists
Write-Host "[4/5] Preparing output directory..." -ForegroundColor Yellow
if (-not (Test-Path "dist")) {
    New-Item -ItemType Directory -Path "dist" | Out-Null
    Write-Host "  Created dist directory" -ForegroundColor Green
} else {
    Write-Host "  Using existing dist directory" -ForegroundColor Green
}

# Step 5: Compile installer
Write-Host "[5/5] Compiling installer with Inno Setup..." -ForegroundColor Yellow
& $iscc "installer.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Installer compilation failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "===== BUILD COMPLETE =====" -ForegroundColor Green
Write-Host ""
Write-Host "Installer created at: dist\PrintagoFolderWatch-Setup.exe" -ForegroundColor Cyan
Write-Host ""
