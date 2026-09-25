# Builds MyDeX for release:
#   dist\MyDeX\                    portable folder (no .NET install needed on the target PC)
#   dist\MyDeX-Setup-<version>.exe installer (if Inno Setup 6 is installed)
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'src\MyDeX\MyDeX.csproj'
$out = Join-Path $repoRoot 'dist\MyDeX'

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "MyDeX $version"

if (-not (Test-Path (Join-Path $repoRoot 'tools\scrcpy\scrcpy.exe'))) {
    Write-Host 'scrcpy is missing - fetching it first.'
    & (Join-Path $PSScriptRoot 'Get-Scrcpy.ps1')
}

if (-not $SkipTests) {
    dotnet test (Join-Path $repoRoot 'tests\MyDeX.Tests') -nologo --artifacts-path (Join-Path $repoRoot 'dist\.test')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed - not publishing.' }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Write-Host "Portable build: $out\MyDeX.exe"

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" (Join-Path $repoRoot 'installer\MyDeX.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed.' }
    Write-Host "Installer: $(Join-Path $repoRoot "dist\MyDeX-Setup-$version.exe")"
} else {
    Write-Host 'Inno Setup 6 not found - skipped the installer (winget install JRSoftware.InnoSetup).'
}

# Checksums to publish next to the downloads, so users can check they got the genuine files.
$sums = Join-Path $repoRoot 'dist\SHA256SUMS.txt'
$files = @(Get-ChildItem (Join-Path $repoRoot 'dist') -Filter "MyDeX-Setup-$version.exe") + @(Get-Item "$out\MyDeX.exe")
$files | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content $sums -Encoding ascii
Write-Host "Checksums: $sums"
Get-Content $sums
