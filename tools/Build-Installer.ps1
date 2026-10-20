#Requires -Version 5.1
<#
.SYNOPSIS
  Builds ProtoLink.Communicator.Windows-Setup-<version>.exe (self-contained publish + Inno Setup).

  Version comes from the csproj <Version> (passed to ISCC as /DMyAppVersion).
  Writes a sibling .release.json. Tagging: docs/release.md

.PARAMETER Upload
  After a successful build, publish the installer via Publish-Release-To-Cloud.ps1 (Files API).

.PARAMETER BaseUrl
  Required with -Upload. Explicit API root (no production default). Example: http://localhost:5000/ or https://protolink.ru/

.PARAMETER Version
  Optional when -Upload is set. When omitted, Publish-Release-To-Cloud.ps1 reads each artifact's sibling .release.json.
#>
param(
    [switch] $Upload,
    [string] $BaseUrl = "",
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
    if (-not $ver) {
        throw "No <Version> found in $Path"
    }
    return [string]$ver
}

function Get-GitCommitOrUnknown {
    param([string] $WorkingDirectory)
    try {
        $sha = & git -C $WorkingDirectory rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $sha -match '^[0-9a-f]{40}$') {
            return $sha.Trim()
        }
    } catch {
        # fall through
    }
    return "unknown"
}

Write-Host "=== ProtoLink Communicator Windows installer build ==="

if (-not (Test-Path $IssPath)) {
    throw "Missing Inno Setup script: $IssPath"
}
if (-not (Test-Path $Csproj)) {
    throw "Missing csproj: $Csproj"
}

$appVersion = Get-ProjectVersion -Path $Csproj
$SetupOut = Join-Path $ArtifactsDir "ProtoLink.Communicator.Windows-Setup-$appVersion.exe"
$ReleaseJsonPath = "$SetupOut.release.json"

Write-Host "csproj Version: $appVersion"
Write-Host "Expected installer: $SetupOut"

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

Write-Host "Step 2/2: Compile installer with $iscc (/DMyAppVersion=$appVersion)"
if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

& $iscc "/DMyAppVersion=$appVersion" $IssPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $SetupOut)) {
    throw "Expected installer not found: $SetupOut"
}

$commit = Get-GitCommitOrUnknown -WorkingDirectory $RepoRoot
$buildDate = [DateTime]::UtcNow.ToString("o")
$releaseMeta = [ordered]@{
    component = "ProtoLink.Communicator.Windows"
    version   = $appVersion
    commit    = $commit
    buildDate = $buildDate
}
$releaseMeta | ConvertTo-Json -Compress | Set-Content -LiteralPath $ReleaseJsonPath -Encoding utf8
Write-Host "Wrote release metadata: $ReleaseJsonPath"

Write-Host ""
Write-Host "Installer ready: $SetupOut"
Get-Item $SetupOut | Format-List FullName, Length, LastWriteTime

if ($Upload) {
    if (-not $BaseUrl) {
        throw "-BaseUrl is required with -Upload (no production default). Example: -BaseUrl http://localhost:5000/ or -BaseUrl https://protolink.ru/"
    }
    Write-Host ""
    Write-Host "Step 3: Publish release to ProtoLink cloud (Files API) at $BaseUrl"
    $uploadArgs = @{
        BaseUrl = $BaseUrl
        WindowsSetupPath = $SetupOut
    }
    if ($Version) {
        $uploadArgs.Version = $Version
    }
    if ($AndroidApkPath) {
        $uploadArgs.AndroidApkPath = $AndroidApkPath
    }
    & $UploadScript @uploadArgs
}
