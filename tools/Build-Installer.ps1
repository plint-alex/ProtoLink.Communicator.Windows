#Requires -Version 5.1
<#
.SYNOPSIS
  Builds ProtoLink.Communicator.Windows-Setup.exe (self-contained publish + Inno Setup).

.PARAMETER Upload
  After a successful build, publish the installer via Publish-Release-To-Cloud.ps1 (Files API).

.PARAMETER Version
  Version string when -Upload is set. Defaults to csproj Version.
#>
param(
    [switch] $Upload,
    [string] $Version = "",
    [string] $AndroidApkPath = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishScript = Join-Path $PSScriptRoot "Publish.ps1"
$UploadScript = Join-Path $PSScriptRoot "Publish-Release-To-Cloud.ps1"
$IssPath = Join-Path $RepoRoot "installer\ProtoLink.Communicator.Windows.iss"
$Csproj = Join-Path $RepoRoot "ProtoLink.Communicator.Windows\ProtoLink.Communicator.Windows.csproj"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"
$SetupOut = Join-Path $ArtifactsDir "ProtoLink.Communicator.Windows-Setup.exe"

function Find-ISCC {
    $candidates = @(
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) {
            return $path
        }
    }

    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    return $null
}

function Get-ProjectVersion {
    param([string] $Path)
    [xml] $xml = Get-Content -LiteralPath $Path
    $ver = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ($ver) { return [string]$ver }
    return "1.0.0"
}

Write-Host "=== ProtoLink Communicator Windows installer build ==="

if (-not (Test-Path $IssPath)) {
    throw "Missing Inno Setup script: $IssPath"
}

Write-Host "Step 1/2: Publish"
& $PublishScript
if ($LASTEXITCODE -ne 0) {
    throw "Publish.ps1 failed with exit code $LASTEXITCODE."
}

$iscc = Find-ISCC
if (-not $iscc) {
    throw @"
Inno Setup 6 was not found (ISCC.exe).

Install it from: https://jrsoftware.org/isinfo.php
Then re-run: .\tools\Build-Installer.ps1
"@
}

Write-Host "Step 2/2: Compile installer with $iscc"
if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

& $iscc $IssPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $SetupOut)) {
    throw "Expected installer not found: $SetupOut"
}

Write-Host ""
Write-Host "Installer ready: $SetupOut"
Get-Item $SetupOut | Format-List FullName, Length, LastWriteTime

if ($Upload) {
    if (-not $Version) {
        $Version = Get-ProjectVersion -Path $Csproj
    }
    Write-Host ""
    Write-Host "Step 3: Publish release $Version to ProtoLink cloud (Files API)"
    $uploadArgs = @{
        Version = $Version
        WindowsSetupPath = $SetupOut
    }
    if ($AndroidApkPath) {
        $uploadArgs.AndroidApkPath = $AndroidApkPath
    }
    & $UploadScript @uploadArgs
}
