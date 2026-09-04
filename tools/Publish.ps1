#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes ProtoLink Communicator (Windows) as a self-contained win-x64 app.
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "artifacts\publish"
$AppProject = Join-Path $RepoRoot "ProtoLink.Communicator.Windows\ProtoLink.Communicator.Windows.csproj"

Write-Host "=== ProtoLink Communicator publish ($Configuration / $Runtime) ==="

if (-not (Test-Path $AppProject)) {
    throw "Missing project: $AppProject"
}

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

Write-Host "Publishing $AppProject -> $PublishDir"
dotnet publish $AppProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $PublishDir "ProtoLink.Communicator.Windows.exe"
if (-not (Test-Path $exe)) {
    throw "Publish output missing ProtoLink.Communicator.Windows.exe"
}

Write-Host "Publish complete: $PublishDir"
Get-Item $exe | Format-List FullName, Length, LastWriteTime
