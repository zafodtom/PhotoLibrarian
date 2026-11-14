param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$destinationRoot = [System.IO.Path]::GetFullPath($Destination)

if (Test-Path $destinationRoot) {
    if (@(Get-ChildItem -LiteralPath $destinationRoot -Force).Count -gt 0) {
        throw "Destination must be an empty directory: $destinationRoot"
    }
} else {
    New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
}

# Export exactly the standalone benchmark utility and its shared inference
# library. Do not delete anything from PhotoLibrarian until the new repository
# has been created, built and verified.
New-Item -ItemType Directory -Path (Join-Path $destinationRoot 'tools') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $destinationRoot 'src') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceRoot 'tools/PhotoLibrarian.ModelBench') `
    -Destination (Join-Path $destinationRoot 'tools/PhotoLibrarian.ModelBench') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'src/PhotoLibrarian.Inference') `
    -Destination (Join-Path $destinationRoot 'src/PhotoLibrarian.Inference') -Recurse -Force

foreach ($file in @('Directory.Build.props', 'Directory.Packages.props', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) `
        -Destination (Join-Path $destinationRoot $file) -Force
}

# Remove build intermediates inadvertently present in a developer checkout.
Get-ChildItem -LiteralPath $destinationRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin', 'obj', '__pycache__') } |
    Sort-Object { $_.FullName.Length } -Descending |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

@'
# PhotoLibrarian ModelBench

Independent development utility extracted from PhotoLibrarian by Trimenor.

Contains the original model comparison CLI, Python model preparation and
calibration tools, and the shared inference layer required to run them.

This tool is **not required** for normal PhotoLibrarian album management or
automatic tag inference. The latter remains in the main PhotoLibrarian app.

## Build

    dotnet build tools/PhotoLibrarian.ModelBench/PhotoLibrarian.ModelBench.csproj -c Release

## Run

    dotnet run --project tools/PhotoLibrarian.ModelBench/PhotoLibrarian.ModelBench.csproj -- --help

See the original project's LICENSE for retained MIT attribution.
'@ | Set-Content -LiteralPath (Join-Path $destinationRoot 'README.md') -Encoding utf8

Write-Host "ModelBench exported to: $destinationRoot"
Write-Host "Verify the build, then create a separate GitHub repository and push these files."
Write-Host "Only after the new repository passes tests should the old benchmark code be removed."
