#Requires -Version 5.1
<#
.SYNOPSIS
  Anonymous gate-D helpers for Publish-Release-To-Cloud.ps1 (no auth header).
#>

function Get-LocalFileSha256 {
    param([Parameter(Mandatory = $true)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-AnonymousCommunicatorView {
    param(
        [Parameter(Mandatory = $true)][string] $BaseUrl
    )
    $base = $BaseUrl.TrimEnd('/') + "/"
    $url = $base + "api/Entities/GetView/communicator?lang=en-US"
    try {
        $resp = Invoke-WebRequest -Uri $url -Method GET -UseBasicParsing -Headers @{ Accept = "application/json" }
    } catch {
        $code = $null
        if ($_.Exception.Response) {
            $code = [int]$_.Exception.Response.StatusCode
        }
        if ($code) {
            throw "Anonymous GetView/communicator failed: HTTP $code (expected 200). URL: $url"
        }
        throw "Anonymous GetView/communicator failed: $($_.Exception.Message). URL: $url"
    }
    if ([int]$resp.StatusCode -ne 200) {
        throw "Anonymous GetView/communicator failed: HTTP $($resp.StatusCode) (expected 200). URL: $url"
    }
    Write-Host "Anonymous GetView/communicator: HTTP 200"
}

function Assert-AnonymousFileMatchesLocal {
    param(
        [Parameter(Mandatory = $true)][string] $BaseUrl,
        [Parameter(Mandatory = $true)][string] $EntityId,
        [Parameter(Mandatory = $true)][string] $FileName,
        [Parameter(Mandatory = $true)][string] $LocalPath,
        [Parameter(Mandatory = $true)][string] $PlatformLabel
    )
    $base = $BaseUrl.TrimEnd('/') + "/"
    $url = "$($base)api/Files/getFile/$EntityId/$FileName"
    $localHash = Get-LocalFileSha256 -Path $LocalPath
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("protolink-gateD-" + [guid]::NewGuid().ToString("N") + "-" + $FileName)
    try {
        try {
            $resp = Invoke-WebRequest -Uri $url -Method GET -OutFile $tmp -UseBasicParsing -PassThru
        } catch {
            $code = $null
            if ($_.Exception.Response) {
                $code = [int]$_.Exception.Response.StatusCode
            }
            if ($code) {
                throw "Anonymous getFile failed for $PlatformLabel`: HTTP $code (expected 200). URL: $url"
            }
            throw "Anonymous getFile failed for $PlatformLabel`: $($_.Exception.Message). URL: $url"
        }
        if ([int]$resp.StatusCode -ne 200) {
            throw "Anonymous getFile failed for $PlatformLabel`: HTTP $($resp.StatusCode) (expected 200). URL: $url"
        }
        $remoteHash = Get-LocalFileSha256 -Path $tmp
        if ($remoteHash -ne $localHash) {
            throw @"
Anonymous getFile SHA256 mismatch for $PlatformLabel.
  URL:    $url
  Local:  $localHash
  Remote: $remoteHash
File may be truncated, wrong blob, or not public. Fix before marking latest.
"@
        }
        return @{
            Platform = $PlatformLabel
            Url = $url
            Sha256 = $localHash
            StatusCode = [int]$resp.StatusCode
        }
    } finally {
        if (Test-Path -LiteralPath $tmp) {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-EntityNameSlotValue {
    param(
        [Parameter(Mandatory = $true)]$Entity,
        [Parameter(Mandatory = $true)][string] $NameSlotId
    )
    $vals = $Entity.values; if (-not $vals) { $vals = $Entity.Values }
    foreach ($v in @($vals)) {
        $parents = $v.parentIds; if (-not $parents) { $parents = $v.ParentIds }
        if (-not $parents) { $parents = $v.parents }
        $parentStrs = @($parents | ForEach-Object { [string]$_ })
        if ($parentStrs -contains $NameSlotId) {
            $raw = $v.value; if ($null -eq $raw) { $raw = $v.Value }
            if ($null -ne $raw) { return [string]$raw }
        }
    }
    return $null
}

function Assert-CloudFileDisplayName {
    param(
        [Parameter(Mandatory = $true)][string] $BaseUrl,
        [Parameter(Mandatory = $true)][string] $Token,
        [Parameter(Mandatory = $true)][string] $EntityId,
        [Parameter(Mandatory = $true)][string] $ExpectedFileName,
        [Parameter(Mandatory = $true)][string] $PlatformLabel,
        [string] $NameSlotId = "00010003-0000-0000-0000-000000000000"
    )
    $base = $BaseUrl.TrimEnd('/') + "/"
    $headers = @{
        Accept = "application/json"
        Authorization = "Bearer $Token"
    }
    $body = @{
        ids = @($EntityId)
        skip = 0
        take = 1
        includeValues = $true
        showHidden = $true
    } | ConvertTo-Json -Depth 8 -Compress
    $ents = Invoke-RestMethod -Method POST -Uri ($base + "api/Entities/GetEntities?fromCache=false") -Headers $headers -ContentType "application/json" -Body $body
    $e = @($ents)[0]
    if (-not $e) {
        throw "CloudFile name assert failed for $PlatformLabel`: entity $EntityId not readable"
    }
    $actual = Get-EntityNameSlotValue -Entity $e -NameSlotId $NameSlotId
    if ($actual -ne $ExpectedFileName) {
        throw @"
CloudFile name slot mismatch for $PlatformLabel (after publish).
  Entity:   $EntityId
  Expected: $ExpectedFileName
  Actual:   $(if ($null -eq $actual) { '(missing)' } else { $actual })
addFile preserves a prior display name; Publish-File must force the leaf name.
"@
    }
    Write-Host "CloudFile name ok for $PlatformLabel`: $actual"
}

function Write-GateDStatusLine {
    param(
        [Parameter(Mandatory = $true)][string] $Platform,
        [Parameter(Mandatory = $true)][string] $Version,
        [string] $Commit = "",
        [Parameter(Mandatory = $true)][string] $Sha256,
        [Parameter(Mandatory = $true)][string] $DownloadUrl,
        [Parameter(Mandatory = $true)][string] $BaseUrl
    )
    $commitPart = if ($Commit) { $Commit } else { "(no release.json commit)" }
    $line = "gate D passed | platform=$Platform | version=$Version | commit=$commitPart | SHA256=$Sha256 | url=$DownloadUrl | base=$BaseUrl | time=$(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    Write-Host $line
    return $line
}
