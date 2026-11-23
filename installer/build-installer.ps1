<#
.SYNOPSIS
    Builds the MobaDesk installer and the matching update manifest (version.json).

.DESCRIPTION
    Publishes MobaDesk (self-contained by default), compiles installer\MobaDesk.iss with
    Inno Setup 6 and writes output\version.json for the in-app updater.

.PARAMETER Version
    Overrides the <Version> value from src\MobaDesk\MobaDesk.csproj.

.PARAMETER PublishDir
    Payload folder. Defaults to publish\win-x64 (self-contained slim build).

.PARAMETER OutputDir
    Installer output folder. Defaults to installer\output.

.PARAMETER BaseUrl
    Public URL of the folder that will host the setup and version.json (for example
    https://downloads.example.com/mobadesk). When omitted, url in version.json is relative
    to the manifest location.

.PARAMETER SkipPublish
    Reuses an existing publish folder instead of running publish.ps1.

.EXAMPLE
    .\installer\build-installer.ps1 -BaseUrl https://downloads.example.com/mobadesk

.EXAMPLE
    .\installer\build-installer.ps1 -SkipPublish -Version 1.0.1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$PublishDir,
    [string]$OutputDir,
    [string]$BaseUrl,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\MobaDesk\MobaDesk.csproj'
$iss = Join-Path $PSScriptRoot 'MobaDesk.iss'

if (-not (Test-Path $project)) { throw "Cannot find $project" }
if (-not (Test-Path $iss)) { throw "Cannot find $iss" }
Write-Host "Repository root: $root"
Write-Host "Build script: $PSCommandPath"

if (-not $Version) {
    [xml]$projectXml = Get-Content $project -Encoding UTF8
    $Version = ($projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if (-not $Version) { $Version = '1.0.0' }
}

if (-not $PublishDir) { $PublishDir = Join-Path $root 'publish\win-x64' }
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot 'output' }
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

# Locate the Inno Setup compiler.
$isccCandidates = @()
foreach ($installRoot in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
    if ($installRoot) {
        foreach ($innoVersion in @('6', '7')) {
            $isccCandidates += (Join-Path $installRoot "Inno Setup $innoVersion\ISCC.exe")
        }
    }
}
if ($env:LOCALAPPDATA) {
    foreach ($innoVersion in @('6', '7')) {
        $isccCandidates += (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup $innoVersion\ISCC.exe")
    }
}
if ($env:TEMP) {
    foreach ($innoVersion in @('6', '7')) {
        $isccCandidates += (Join-Path $env:TEMP "InnoSetup$innoVersion\ISCC.exe")
    }
}
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    # Fallback: any "Inno Setup *" folder (covers future major versions / chocolatey layouts).
    foreach ($installRoot in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if (-not $installRoot -or -not (Test-Path $installRoot)) { continue }
        $found = Get-ChildItem -Path $installRoot -Directory -Filter 'Inno Setup *' -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($found) { $iscc = $found; break }
    }
}
if (-not $iscc) {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { $iscc = $onPath.Source }
}
if (-not $iscc) {
    Write-Host 'Inno Setup 6 compiler (ISCC.exe) was not found.' -ForegroundColor Yellow
    Write-Host 'Install it with one of the following, then re-run this script:'
    Write-Host '  winget install --id JRSoftware.InnoSetup -e'
    Write-Host '  https://jrsoftware.org/isdl.php'
    exit 1
}

if (-not $SkipPublish) {
    $publishScript = Join-Path $root 'publish.ps1'
    if (-not (Test-Path $publishScript)) { throw "Cannot find publish script: $publishScript" }
    Write-Host 'Publishing MobaDesk (self-contained, slim)...' -ForegroundColor Cyan
    & $publishScript
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not (Test-Path (Join-Path $PublishDir 'MobaDesk.exe'))) {
    throw "Publish payload not found at $PublishDir. Run without -SkipPublish first."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Write-Host "Compiling installer (version $Version)..." -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" "/DAppPublishDir=$PublishDir" "/DOutputDir=$OutputDir" $iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setupName = "MobaDesk-Setup-$Version.exe"
$setupPath = Join-Path $OutputDir $setupName
if (-not (Test-Path $setupPath)) { throw "Installer was not produced: $setupPath" }

$hash = (Get-FileHash -Algorithm SHA256 -Path $setupPath).Hash
$size = (Get-Item $setupPath).Length
$url = $setupName
if ($BaseUrl) { $url = ($BaseUrl.TrimEnd('/') + '/' + $setupName) }

$manifest = [ordered]@{
    version     = $Version
    url         = $url
    sha256      = $hash
    size        = $size
    notes       = ''
    publishedAt = (Get-Date).ToUniversalTime().ToString('o')
}
$manifestPath = Join-Path $OutputDir 'version.json'
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host ''
Write-Host 'Installer ready:' -ForegroundColor Green
Write-Host "  $setupPath"
Write-Host "  size:   $([Math]::Round($size / 1MB, 1)) MB"
Write-Host "  sha256: $hash"
Write-Host "Manifest:" -ForegroundColor Green
Write-Host "  $manifestPath"
Write-Host ''
Write-Host 'Upload both files to the update feed folder. MobaDesk checks version.json,'
Write-Host 'downloads url, verifies sha256 and runs the installer with /VERYSILENT.'
