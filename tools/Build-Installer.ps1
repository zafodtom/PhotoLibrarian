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
$issFile = Join-Path $repoRoot 'packaging\PhotoLibrarian.iss'

$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repoRoot $OutputDirectory
}

$publishDir = Join-Path $outputRoot 'publish\win-x64'
$installerDir = Join-Path $outputRoot 'installer'

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item $publishDir -ItemType Directory -Force | Out-Null
New-Item $installerDir -ItemType Directory -Force | Out-Null

Write-Host "Publishing PhotoLibrarian $Version (win-x64, self-contained)..."
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
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

foreach ($requiredFile in @(
    'PhotoLibrarian.exe',
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
