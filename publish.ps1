$ErrorActionPreference = "Stop"

$Root = $PSScriptRoot
$ReleaseDir = Join-Path $Root "release"

Write-Host "=== Deploy Paladin Release Build ===" -ForegroundColor Cyan
Write-Host ""

# Clean previous release
if (Test-Path $ReleaseDir) {
    Write-Host "Cleaning previous release..." -ForegroundColor Yellow
    Remove-Item $ReleaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ReleaseDir | Out-Null

# Build solution first to catch errors early
Write-Host "Building solution..." -ForegroundColor Green
dotnet build (Join-Path $Root "DeployPaladin.slnx") -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "  Build OK" -ForegroundColor DarkGreen

# Publish installer engine (all files needed — it's the base for every setup.exe)
Write-Host "Publishing installer engine..." -ForegroundColor Green
$InstallerDir = Join-Path $ReleaseDir "installer"
dotnet publish (Join-Path $Root "DeployPaladin.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $InstallerDir `
    --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }

$InstallerExe = Join-Path $InstallerDir "DeployPaladin.exe"
$InstallerSize = (Get-Item $InstallerExe).Length
Write-Host ("  DeployPaladin.exe OK ({0:N1} MB)" -f ($InstallerSize / 1MB)) -ForegroundColor DarkGreen

# Publish builder CLI (single file, self-contained)
Write-Host "Publishing builder CLI..." -ForegroundColor Green
$BuilderDir = Join-Path $ReleaseDir "builder"
dotnet publish (Join-Path $Root "DeployPaladin.Builder\DeployPaladin.Builder.csproj") `
    -c Release `
    -o $BuilderDir `
    --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Builder publish failed" }

$BuilderExe = Join-Path $BuilderDir "DeployPaladin.Builder.exe"
$BuilderSize = (Get-Item $BuilderExe).Length
Write-Host ("  DeployPaladin.Builder.exe OK ({0:N1} MB)" -f ($BuilderSize / 1MB)) -ForegroundColor DarkGreen

# Summary
Write-Host ""
Write-Host "=== Release Ready ===" -ForegroundColor Cyan
Write-Host "  $ReleaseDir" -ForegroundColor White
Write-Host ""
Write-Host "  release\" -ForegroundColor White
Write-Host "    builder\" -ForegroundColor Gray
Write-Host ("      DeployPaladin.Builder.exe   {0:N1} MB  (standalone CLI)" -f ($BuilderSize / 1MB))
Write-Host "    installer\" -ForegroundColor Gray
Write-Host ("      DeployPaladin.exe           {0:N1} MB  (base installer engine)" -f ($InstallerSize / 1MB))
Write-Host ""
Write-Host "Usage:" -ForegroundColor Yellow
Write-Host '  .\release\builder\DeployPaladin.Builder.exe --payload .\MyApp --base .\release\installer\DeployPaladin.exe --output .\MyApp_Setup.exe'
