param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Manufacturer = "YourCompany",
    [string]$Version = "1.0.0.0"
)

$ErrorActionPreference = "Stop"
$root         = $PSScriptRoot
$publishDir   = Join-Path $root "publish"
$installerDir = Join-Path $root "installer"
$msiPath      = Join-Path $installerDir "PiiScannerAgentSetup.msi"

Write-Host "==> Publishing PiiScanner.exe ($Runtime, $Configuration)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "PiiScanner\PiiScanner.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $publishDir

Write-Host "==> Building MSI..." -ForegroundColor Cyan
wix eula accept wix7
wix build (Join-Path $installerDir "Package.wxs") `
    -arch x64 `
    -d PublishDir=$publishDir `
    -d Manufacturer=$Manufacturer `
    -d Version=$Version `
    -out $msiPath

Write-Host "==> Done: $msiPath" -ForegroundColor Green