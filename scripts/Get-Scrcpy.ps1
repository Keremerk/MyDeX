# Downloads the latest official scrcpy for Windows (includes adb) into tools\scrcpy.
# Source: https://github.com/Genymobile/scrcpy/releases
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $repoRoot 'tools\scrcpy'

$release = Invoke-RestMethod 'https://api.github.com/repos/Genymobile/scrcpy/releases/latest' -Headers @{ 'User-Agent' = 'MyDeX' }
$asset = $release.assets | Where-Object { $_.name -like 'scrcpy-win64-*.zip' } | Select-Object -First 1
if (-not $asset) { throw "No Windows 64-bit build found in scrcpy $($release.tag_name)." }

$zip = Join-Path $env:TEMP $asset.name
Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)..."
Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing

# GitHub publishes a SHA-256 digest for every release asset.
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
if ($asset.digest -match '^sha256:([0-9a-f]{64})$') {
    if ($Matches[1] -ne $hash) { Remove-Item $zip -Force; throw "Checksum mismatch for $($asset.name) - download discarded." }
    Write-Host 'Checksum OK.'
} else {
    Write-Host "No published checksum found; SHA-256 is $hash"
}

$extract = Join-Path $env:TEMP 'mydex-scrcpy-extract'
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive $zip $extract -Force
$inner = Get-ChildItem $extract -Directory | Select-Object -First 1

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
Move-Item $inner.FullName $dest
Remove-Item $zip, $extract -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "scrcpy $($release.tag_name) is ready in $dest"
