[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use major.minor.patch format, for example 1.0.0.'
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'src\PhotoLibrarian\PhotoLibrarian.csproj'
$helperProject = Join-Path $repoRoot 'src\PhotoLibrarian.UpdateHelper\PhotoLibrarian.UpdateHelper.csproj'
$issFile = Join-Path $repoRoot 'packaging\PhotoLibrarian.iss'

$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repoRoot $OutputDirectory
}

$publishDir = Join-Path $outputRoot 'publish\win-x64'
$helperPublishDir = Join-Path $outputRoot 'publish\updater-win-x64'
$installerDir = Join-Path $outputRoot 'installer'

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item $publishDir -ItemType Directory -Force | Out-Null
New-Item $installerDir -ItemType Directory -Force | Out-Null

Write-Host "Publishing PhotoLibrarian $Version (win-x64, self-contained)..."
$informationalVersion = if ($env:DEV_RELEASE_TAG) {
    $env:DEV_RELEASE_TAG.TrimStart('v')
} else {
    $Version
}
dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:EnableMsixTooling=true `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:Version=$Version `
    -p:InformationalVersion=$informationalVersion `
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Publish the out-of-process update helper to a single portable executable.
# It is copied to a temporary folder before execution so Inno can replace
# the installed app binaries while the helper remains running.
if (Test-Path $helperPublishDir) {
    Remove-Item $helperPublishDir -Recurse -Force
}
Write-Host 'Publishing standalone update helper (win-x64)...'
dotnet publish $helperProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $helperPublishDir
if ($LASTEXITCODE -ne 0) {
    throw "Update helper publish failed with exit code $LASTEXITCODE."
}
$helperExe = Join-Path $helperPublishDir 'PhotoLibrarian.UpdateHelper.exe'
if (-not (Test-Path $helperExe)) {
    throw 'The update helper executable is missing from the published output.'
}
Copy-Item $helperExe (Join-Path $publishDir 'PhotoLibrarian.UpdateHelper.exe') -Force

foreach ($requiredFile in @(
    'PhotoLibrarian.exe',
    'PhotoLibrarian.UpdateHelper.exe',
    'coreclr.dll',
    'Microsoft.UI.Xaml.dll',
    'App.xbf',
    'MainWindow.xbf'
)) {
    if (-not (Test-Path (Join-Path $publishDir $requiredFile))) {
        throw "Published application is missing required file: $requiredFile"
    }
}

$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Source -First 1

if (-not $iscc) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }

    $iscc = $candidates | Select-Object -First 1
}

if (-not $iscc) {
    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 or add ISCC.exe to PATH.'
}

$publishForInno = $publishDir.Replace('/', '\')
$outputForInno = $installerDir.Replace('/', '\')

Write-Host "Building installer with $iscc ..."
& $iscc `
    "/DMyAppVersion=$Version" `
    "/DPublishDir=$publishForInno" `
    "/DOutputDir=$outputForInno" `
    $issFile

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $installerDir "PhotoLibrarian-$Version-Setup-x64.exe"
if (-not (Test-Path $installer)) {
    throw "Installer was not created at expected path: $installer"
}

$hash = Get-FileHash $installer -Algorithm SHA256

Write-Host ''
Write-Host 'Installer ready:'
Write-Host "  $installer"
Write-Host 'SHA256:'
Write-Host "  $($hash.Hash)"
