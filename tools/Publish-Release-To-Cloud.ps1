#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes Communicator installer/APK to ProtoLink cloud via Files API and updates release version values.

.DESCRIPTION
  Does not use MinIO access keys for the Files API path. Authenticates to ProtoLink.Api, resolves
  CloudFile and per-platform version entities (from communicator/ids.generated.json or by scanning
  release children), uploads via addFile, and writes each uploaded platform's version into
  communicator-version-windows / communicator-version-android (not the release name slot).
  Optional -Notes go to the release entity description slot only.

  Cycle-2 / D10: when a platform's version entity semver changes, the current latest CloudFile blob
  is snapshotted into communicator-release-history (public read, platform parent, versioned name)
  and that platform's archive is pruned to at most 5 rows, before overwriting the stable latest id.

  -BaseUrl is required (no production default). Production:
    -BaseUrl https://protolink.ru/
  -Version is optional; when omitted, each artifact's sibling <artifact>.release.json supplies it.
  Windows and Android uploads are independent: omit a path / use -SkipWindows for Android-only.
  After upload, gate D runs by default: anonymous getFile SHA256 match + anonymous GetView/communicator.
  Use -SkipVerify only for emergencies (skips those checks).

.EXAMPLE
  .\tools\Publish-Release-To-Cloud.ps1 -BaseUrl http://localhost:5000/

.EXAMPLE
  .\tools\Publish-Release-To-Cloud.ps1 -BaseUrl https://protolink.ru/ -SkipWindows -AndroidApkPath .\artifacts\ProtoLink.Communicator.Android-1.0.3.apk

.EXAMPLE
  .\tools\Publish-Release-To-Cloud.ps1 -BaseUrl http://localhost:5000/ -Version 1.0.7

.EXAMPLE
  .\tools\Publish-Release-To-Cloud.ps1 -BaseUrl http://localhost:5000/ -SkipVerify
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [string] $Version = "",

    [string] $WindowsSetupPath = "",

    [string] $AndroidApkPath = "",

    [switch] $SkipWindows,

    [switch] $SkipAndroid,

    [string] $Notes = "",

    [string] $Login = "admin",

    [string] $Password = "admin",

    [int] $GiveinPlaceId = 0,

    [string] $IdsFile = "",

    [string] $MinioEndpoint = "plintec.ru:9000",

    [string] $MinioAccessKey = "minioadmin",

    [string] $MinioSecretKey = "minioadmin",

    [string] $MinioBucket = "protolink-files",

    [switch] $SkipVerify
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Publish-Release-Verify.ps1")

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsDir = Join-Path $RepoRoot "artifacts"
$WindowsNamePrefix = "ProtoLink.Communicator.Windows-Setup"
$AndroidNamePrefix = "ProtoLink.Communicator.Android"

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw @"
-BaseUrl is required. There is no default (including production).

Examples:
  -BaseUrl http://localhost:5000/
  -BaseUrl https://protolink.ru/
"@
}

function Find-NewestArtifact {
    param([string] $Directory, [string] $Filter)
    if (-not (Test-Path -LiteralPath $Directory)) { return "" }
    # Exclude sibling meta JSON and LocalStand stubs (*-stand*) so a careless default
    # publish cannot overwrite public latest with a stand build. Prefer explicit paths.
    $hit = Get-ChildItem -LiteralPath $Directory -Filter $Filter -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "*.release.json" -and $_.Name -notlike "*-stand*" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return ""
}

function Get-ReleaseMetaOrNull {
    param([string] $ArtifactPath)
    $jsonPath = "$ArtifactPath.release.json"
    if (-not (Test-Path -LiteralPath $jsonPath)) { return $null }
    $meta = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    if (-not $meta.version) {
        throw "release.json missing 'version': $jsonPath"
    }
    return $meta
}

function Resolve-ArtifactVersion {
    param(
        [string] $ArtifactPath,
        [string] $RequestedVersion
    )
    $meta = Get-ReleaseMetaOrNull -ArtifactPath $ArtifactPath
    if ($meta) {
        $fromJson = [string]$meta.version
        if ($RequestedVersion -and ($RequestedVersion -ne $fromJson)) {
            throw "-Version '$RequestedVersion' disagrees with release.json version '$fromJson' for $ArtifactPath"
        }
        return @{ Version = $fromJson; Meta = $meta }
    }
    if (-not $RequestedVersion) {
        throw "Missing $($ArtifactPath).release.json and -Version was not set. Build the versioned artifact (writes release.json) or pass -Version explicitly."
    }
    Write-Host "WARNING: no release.json beside $ArtifactPath; using -Version $RequestedVersion"
    return @{ Version = $RequestedVersion; Meta = $null }
}

if (-not $IdsFile) {
    $IdsFile = $env:PROTOLINK_VIEWEDITOR_IDS
}
if (-not $IdsFile) {
    $sibling = Join-Path (Split-Path $RepoRoot -Parent) "ProtoLink.ViewEditor\communicator\ids.generated.json"
    if (Test-Path $sibling) {
        $IdsFile = $sibling
    } else {
        $IdsFile = "D:\- Projects\ProtoLink.ViewEditor\communicator\ids.generated.json"
    }
}

$NameSlot = "00010003-0000-0000-0000-000000000000"
$DescSlot = "00010004-0000-0000-0000-000000000000"
$MimeSlot = "00010012-0000-0000-0000-000000000000"
$EnUs = "00010002-0002-0000-0000-000000000000"
$PublicId = "00000000-0000-0000-0000-000000000001"
$BlobUploader = Join-Path $PSScriptRoot "CloudBlobUpload\CloudBlobUpload.csproj"
$ArchiveRulesProject = Join-Path $PSScriptRoot "PublishReleaseArchive\PublishReleaseArchive.csproj"
$MaxArchivePerPlatform = 5

$BaseUrl = $BaseUrl.TrimEnd('/') + "/"

if (-not $SkipWindows -and -not $WindowsSetupPath) {
    $WindowsSetupPath = Find-NewestArtifact -Directory $ArtifactsDir -Filter "$WindowsNamePrefix-*.exe"
}
if (-not $SkipAndroid -and -not $AndroidApkPath) {
    $AndroidApkPath = Find-NewestArtifact -Directory $ArtifactsDir -Filter "$AndroidNamePrefix-*.apk"
}

$uploadWindows = (-not $SkipWindows) -and $WindowsSetupPath -and (Test-Path -LiteralPath $WindowsSetupPath)
$uploadAndroid = (-not $SkipAndroid) -and $AndroidApkPath -and (Test-Path -LiteralPath $AndroidApkPath)

if (-not $SkipWindows -and $WindowsSetupPath -and -not (Test-Path -LiteralPath $WindowsSetupPath)) {
    throw "Windows setup not found: $WindowsSetupPath (expected versioned name $WindowsNamePrefix-<version>.exe under artifacts/)"
}
if (-not $SkipAndroid -and $AndroidApkPath -and -not (Test-Path -LiteralPath $AndroidApkPath)) {
    throw "Android APK not found: $AndroidApkPath (expected versioned name $AndroidNamePrefix-<versionName>.apk under artifacts/)"
}
if (-not $uploadWindows -and -not $uploadAndroid) {
    throw @"
Nothing to upload. Provide a Windows setup and/or Android APK, or place versioned artifacts under:
  $ArtifactsDir\$WindowsNamePrefix-<version>.exe
  $ArtifactsDir\$AndroidNamePrefix-<versionName>.apk
Use -SkipWindows for Android-only or -SkipAndroid for Windows-only. Pass -WindowsSetupPath / -AndroidApkPath to override defaults.
"@
}

$windowsMeta = $null
$androidMeta = $null
$windowsVersion = $null
$androidVersion = $null

if ($uploadWindows) {
    $resolved = Resolve-ArtifactVersion -ArtifactPath $WindowsSetupPath -RequestedVersion $Version
    $windowsVersion = [string]$resolved.Version
    $windowsMeta = $resolved.Meta
}
if ($uploadAndroid) {
    $resolved = Resolve-ArtifactVersion -ArtifactPath $AndroidApkPath -RequestedVersion $Version
    $androidVersion = [string]$resolved.Version
    $androidMeta = $resolved.Meta
}

# Platforms may differ (Windows 1.0.7 vs Android 1.0.3). Only enforce -Version agreement per artifact (Resolve-ArtifactVersion).

function Invoke-Json {
    param([string] $Method, [string] $Url, [object] $Body = $null, [string] $Token = "")
    $headers = @{ Accept = "application/json" }
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = $Url; Headers = $headers; ContentType = "application/json" }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress) }
    return Invoke-RestMethod @params
}

function Test-WindowsArtifactName {
    param([string] $FileName)
    if (-not $FileName) { return $false }
    return ($FileName -eq "$WindowsNamePrefix.exe") -or (
        $FileName.StartsWith("$WindowsNamePrefix-") -and $FileName.EndsWith(".exe")
    )
}

function Test-AndroidArtifactName {
    param([string] $FileName)
    if (-not $FileName) { return $false }
    return ($FileName -eq "$AndroidNamePrefix.apk") -or (
        $FileName.StartsWith("$AndroidNamePrefix-") -and $FileName.EndsWith(".apk")
    )
}

Write-Host "=== Publish Communicator release to ProtoLink cloud ==="
Write-Host "API: $BaseUrl"
if ($uploadWindows) { Write-Host "Windows: $WindowsSetupPath (version $windowsVersion)" } else { Write-Host "Windows: (skipped)" }
if ($uploadAndroid) { Write-Host "Android: $AndroidApkPath (version $androidVersion)" } else { Write-Host "Android: (skipped)" }

$loginResp = Invoke-Json -Method POST -Url ($BaseUrl + "api/Authentication/login") -Body @{
    login = $Login
    password = $Password
    giveinPlaceId = $GiveinPlaceId
}
$token = $loginResp.accessToken
if (-not $token) { $token = $loginResp.AccessToken }
if (-not $token) { throw "Login failed" }

$ids = $null
if (Test-Path $IdsFile) {
    $ids = Get-Content -LiteralPath $IdsFile -Raw | ConvertFrom-Json
    Write-Host "Loaded ids from $IdsFile"
}

$releaseId = $null
$releaseHistoryId = $null
$artifactWindowsId = $null
$artifactAndroidId = $null
$versionWindowsId = $null
$versionAndroidId = $null
$platformWindowsId = $null
$platformAndroidId = $null

if ($ids) {
    $releaseId = [string]$ids.release
    $artifactWindowsId = [string]$ids.artifactWindows
    $artifactAndroidId = [string]$ids.artifactAndroid
    if ($ids.PSObject.Properties.Name -contains "releaseHistory") {
        $releaseHistoryId = [string]$ids.releaseHistory
    }
    if ($ids.PSObject.Properties.Name -contains "versionWindows") {
        $versionWindowsId = [string]$ids.versionWindows
    }
    if ($ids.PSObject.Properties.Name -contains "versionAndroid") {
        $versionAndroidId = [string]$ids.versionAndroid
    }
    if ($ids.PSObject.Properties.Name -contains "platformWindows") {
        $platformWindowsId = [string]$ids.platformWindows
    }
    if ($ids.PSObject.Properties.Name -contains "platformAndroid") {
        $platformAndroidId = [string]$ids.platformAndroid
    }
    if ($ids.PSObject.Properties.Name -contains "public") {
        $fromIds = [string]$ids.public
        if ($fromIds) { $PublicId = $fromIds }
    }
}

if (-not $releaseId -or -not $releaseHistoryId) {
    # Resolve host by GetView/code then children
    $viewDoc = Invoke-RestMethod -Method GET -Uri ($BaseUrl + "api/Entities/GetView/communicator?lang=en-US") -Headers @{ Accept = "application/json" }
    $mappings = $viewDoc.entityViews
    if (-not $mappings) { $mappings = $viewDoc.EntityViews }
    if (-not $mappings -or $mappings.Count -eq 0) {
        throw "Host entity 'communicator' not found. Run Apply-CommunicatorPage.ps1 first."
    }
    $hostId = [string]$mappings[0].entityId
    if (-not $hostId) { $hostId = [string]$mappings[0].EntityId }

    $children = Invoke-Json -Method POST -Url ($BaseUrl + "api/Entities/GetEntities?fromCache=false") -Token $token -Body @{
        parentIds = @($hostId)
        skip = 0
        take = 200
        includeValues = $true
        showHidden = $true
    }
    foreach ($c in @($children)) {
        $code = $c.code; if (-not $code) { $code = $c.Code }
        $id = [string]$c.id; if (-not $id) { $id = [string]$c.Id }
        if ($code -eq "communicator-release-latest" -and -not $releaseId) {
            $releaseId = $id
        }
        if ($code -eq "communicator-release-history" -and -not $releaseHistoryId) {
            $releaseHistoryId = $id
        }
    }
}

if (-not $releaseId) { throw "communicator-release-latest not found. Run Apply-CommunicatorPage.ps1 first." }
if (($uploadWindows -or $uploadAndroid) -and -not $releaseHistoryId) {
    throw "communicator-release-history not found. Run Apply-CommunicatorPage.ps1 first (D10 history folder)."
}

if ((-not $platformWindowsId -and $uploadWindows) -or (-not $platformAndroidId -and $uploadAndroid)) {
    # Platform dimension lives under communicator-catalog (sibling taxonomy), not under host.
    $catalogList = Invoke-Json -Method POST -Url ($BaseUrl + "api/Entities/GetEntities?fromCache=false") -Token $token -Body @{
        parentIds = @($PublicId)
        skip = 0
        take = 200
        includeValues = $false
        showHidden = $true
    }
    $catalogId = $null
    foreach ($c in @($catalogList)) {
        $code = $c.code; if (-not $code) { $code = $c.Code }
        if ($code -eq "communicator-catalog") {
            $catalogId = [string]$c.id; if (-not $catalogId) { $catalogId = [string]$c.Id }
            break
        }
    }
    if ($catalogId) {
        $dims = Invoke-Json -Method POST -Url ($BaseUrl + "api/Entities/GetEntities?fromCache=false") -Token $token -Body @{
            parentIds = @($catalogId)
            skip = 0
            take = 50
            includeValues = $false
            showHidden = $true
        }
        $dimPlatformId = $null
        foreach ($d in @($dims)) {
            $code = $d.code; if (-not $code) { $code = $d.Code }
            if ($code -eq "communicator-dim-platform") {
                $dimPlatformId = [string]$d.id; if (-not $dimPlatformId) { $dimPlatformId = [string]$d.Id }
                break
            }
        }
        if ($dimPlatformId) {
            $plats = Invoke-Json -Method POST -Url ($BaseUrl + "api/Entities/GetEntities?fromCache=false") -Token $token -Body @{
                parentIds = @($dimPlatformId)
                skip = 0
                take = 20
                includeValues = $false
                showHidden = $true
            }
            foreach ($p in @($plats)) {
                $code = $p.code; if (-not $code) { $code = $p.Code }
                $id = [string]$p.id; if (-not $id) { $id = [string]$p.Id }
                if ($code -eq "communicator-platform-windows" -and -not $platformWindowsId) { $platformWindowsId = $id }
                if ($code -eq "communicator-platform-android" -and -not $platformAndroidId) { $platformAndroidId = $id }
            }
        }
    }
}

if ($uploadWindows -and -not $platformWindowsId) {
    throw "communicator-platform-windows not found. Run Apply-CommunicatorPage.ps1 first."
}
if ($uploadAndroid -and -not $platformAndroidId) {
    throw "communicator-platform-android not found. Run Apply-CommunicatorPage.ps1 first."
}

$needWindowsId = $uploadWindows -and -not $artifactWindowsId
$needAndroidId = $uploadAndroid -and -not $artifactAndroidId
$needVersionWindowsId = $uploadWindows -and -not $versionWindowsId
$needVersionAndroidId = $uploadAndroid -and -not $versionAndroidId
if ($needWindowsId -or $needAndroidId -or $needVersionWindowsId -or $needVersionAndroidId) {
    $rels = Invoke-Json -Method POST -Url ($BaseUrl + "api/Entities/GetEntities?fromCache=false") -Token $token -Body @{
        parentIds = @($releaseId)
        skip = 0
        take = 200
        includeValues = $true
        showHidden = $true
    }
    foreach ($c in @($rels)) {
        $code = $c.code; if (-not $code) { $code = $c.Code }
        $id = [string]$c.id; if (-not $id) { $id = [string]$c.Id }
        if ($code -eq "communicator-version-windows" -and -not $versionWindowsId) {
            $versionWindowsId = $id
            continue
        }
        if ($code -eq "communicator-version-android" -and -not $versionAndroidId) {
            $versionAndroidId = $id
            continue
        }
        if ($code -ne "CloudFile") { continue }
        $vals = $c.values; if (-not $vals) { $vals = $c.Values }
        $fileName = $null
        foreach ($v in @($vals)) {
            $raw = $v.value; if ($null -eq $raw) { $raw = $v.Value }
            if ($raw -is [string] -and ($raw.EndsWith(".exe") -or $raw.EndsWith(".apk"))) {
                $fileName = $raw
                break
            }
        }
        if ((Test-WindowsArtifactName $fileName) -and -not $artifactWindowsId) { $artifactWindowsId = $id }
        elseif ((Test-AndroidArtifactName $fileName) -and -not $artifactAndroidId) { $artifactAndroidId = $id }
    }
}

if ($uploadWindows -and -not $artifactWindowsId) { throw "Windows CloudFile artifact not found under release. Run Apply-CommunicatorPage.ps1 first." }
if ($uploadAndroid -and -not $artifactAndroidId) { throw "Android CloudFile artifact not found under release. Run Apply-CommunicatorPage.ps1 first." }
if ($uploadWindows -and -not $versionWindowsId) {
    throw "communicator-version-windows not found under release. Run Apply-CommunicatorPage.ps1 first."
}
if ($uploadAndroid -and -not $versionAndroidId) {
    throw "communicator-version-android not found under release. Run Apply-CommunicatorPage.ps1 first."
}

function ConvertTo-ViewIdList {
    param($Entity)
    # Build a concrete empty Object[] in-place. Do not `return @()` (unwraps to $null and
    # omits viewIds in JSON) and do not wrap the result again in `@(...)` at the call site
    # (that becomes [[]] and Guid binding fails).
    $vids = [System.Collections.Generic.List[object]]::new()
    $raw = $Entity.viewIds; if ($null -eq $raw) { $raw = $Entity.ViewIds }
    if ($null -ne $raw -and $raw -ne "") {
        if ($raw -is [string]) {
            foreach ($part in @($raw.Split(",") | Where-Object { $_ -and $_.Trim() })) {
                $vids.Add($part.Trim())
            }
        } else {
            foreach ($part in @($raw)) {
                if ($null -ne $part -and "$part" -ne "") { $vids.Add($part) }
            }
        }
    }
    return , $vids.ToArray()
}

function Set-CloudFileUploadMetadata {
    param(
        [string] $EntityId,
        [string] $FileName,
        [string] $ContentType,
        [string] $Token,
        [string] $Base
    )
    # addFile keeps a non-synthetic existing name (API design for sync). Publish must force the
    # leaf name so Apply exact-name reuse and download labels match the uploaded artifact.
    # Mime + name in one UpdateEntity so both land on the same version (sequential AddValue
    # would leave only the last slot visible on GetEntities).
    $ents = Invoke-Json -Method POST -Url ($Base + "api/Entities/GetEntities?fromCache=false") -Token $Token -Body @{
        ids = @($EntityId)
        skip = 0
        take = 1
        includeValues = $false
        showHidden = $true
    }
    $e = @($ents)[0]
    if (-not $e) { throw "CloudFile entity $EntityId not readable before mime/name UpdateEntity" }
    $code = $e.code; if (-not $code) { $code = $e.Code }
    $order = 0
    if ($null -ne $e.order) { $order = [int]$e.order } elseif ($null -ne $e.Order) { $order = [int]$e.Order }
    $hidden = $false
    if ($null -ne $e.hidden) { $hidden = [bool]$e.hidden } elseif ($null -ne $e.Hidden) { $hidden = [bool]$e.Hidden } elseif ($null -ne $e.Hiddenn) { $hidden = [bool]$e.Hiddenn }
    $mainParent = $null
    if ($e.mainParentId) { $mainParent = [string]$e.mainParentId } elseif ($e.MainParentId) { $mainParent = [string]$e.MainParentId }
    $viewIds = ConvertTo-ViewIdList $e
    Invoke-Json -Method POST -Url ($Base + "api/Entities/UpdateEntity") -Token $Token -Body @{
        id = $EntityId
        name = $FileName
        description = ""
        code = $code
        order = $order
        hidden = $hidden
        mainParentId = $mainParent
        viewIds = $viewIds
        values = @(
            @{
                type = 0
                order = 0
                hidden = $false
                parentIds = @($MimeSlot)
                value = $ContentType
            },
            @{
                type = 0
                order = 1
                hidden = $false
                parentIds = @($NameSlot)
                value = $FileName
            }
        )
    } | Out-Null
    Write-Host "  mimeType/name values updated on entity -> $FileName"
}

function Invoke-ArchiveRulesCli {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $CliArgs)
    if (-not (Test-Path -LiteralPath $ArchiveRulesProject)) {
        throw "Missing $ArchiveRulesProject"
    }
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $argList = @("run", "--project", $ArchiveRulesProject, "-c", "Release", "--no-launch-profile", "--") + @($CliArgs)
        $output = & dotnet @argList 2>&1
        $exit = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEap
    }
    if ($exit -ne 0) {
        throw "PublishReleaseArchive failed ($exit): $($output | Out-String)"
    }
    return @($output | ForEach-Object { "$_" } | Where-Object { $_ -ne $null })
}

function Test-ShouldSnapshotVersion {
    param([string] $CurrentVersion, [string] $NewVersion)
    # Match ReleaseArchiveRules.ShouldSnapshot: empty/whitespace → false.
    # Also avoid calling the CLI with an empty argv: Windows PowerShell 5.1 strips
    # empty native args, so `dotnet … should-snapshot "" 1.0.x` arrives as 2 args
    # and the CLI used to exit 1 (usage). pwsh 7 keeps the empty arg.
    if ([string]::IsNullOrWhiteSpace($CurrentVersion) -or [string]::IsNullOrWhiteSpace($NewVersion)) {
        return $false
    }
    $lines = Invoke-ArchiveRulesCli -CliArgs @("should-snapshot", $CurrentVersion, $NewVersion)
    $answer = ($lines | Select-Object -Last 1).Trim().ToLowerInvariant()
    return $answer -eq "true"
}

function Get-SemverFromArtifactFileName {
    param([string] $FileName)
    $lines = Invoke-ArchiveRulesCli -CliArgs @("parse-semver", $FileName)
    $v = ($lines | Select-Object -Last 1).Trim()
    if (-not $v) { return $null }
    return $v
}

function Get-ValueParentIdStrings {
    param($ValueRow)
    # GetEntities returns value.parents; some contracts use parentIds.
    $parents = $ValueRow.parents
    if (-not $parents) { $parents = $ValueRow.Parents }
    if (-not $parents) { $parents = $ValueRow.parentIds }
    if (-not $parents) { $parents = $ValueRow.ParentIds }
    return @($parents | ForEach-Object { [string]$_ })
}

function Get-RemoteEntityNameSlotValue {
    # Named distinctly from Publish-Release-Verify.ps1 Get-EntityNameSlotValue (Entity object).
    param([string] $EntityId, [string] $Token, [string] $Base)
    $ents = Invoke-Json -Method POST -Url ($Base + "api/Entities/GetEntities?fromCache=false") -Token $Token -Body @{
        ids = @($EntityId)
        skip = 0
        take = 1
        includeValues = $true
        showHidden = $true
    }
    $e = @($ents)[0]
    if (-not $e) { return $null }
    $vals = $e.values; if (-not $vals) { $vals = $e.Values }
    foreach ($v in @($vals)) {
        $parentStrs = Get-ValueParentIdStrings -ValueRow $v
        if ($parentStrs -contains $NameSlot) {
            $raw = $v.value; if ($null -eq $raw) { $raw = $v.Value }
            if ($raw -is [string]) { return $raw }
        }
    }
    return $null
}

function Get-CloudFileLeafAndMime {
    param([string] $EntityId, [string] $Token, [string] $Base)
    $ents = Invoke-Json -Method POST -Url ($Base + "api/Entities/GetEntities?fromCache=false") -Token $Token -Body @{
        ids = @($EntityId)
        skip = 0
        take = 1
        includeValues = $true
        showHidden = $true
    }
    $e = @($ents)[0]
    if (-not $e) { return $null }
    $vals = $e.values; if (-not $vals) { $vals = $e.Values }
    $fileName = $null
    $mime = "application/octet-stream"
    foreach ($v in @($vals)) {
        $parentStrs = Get-ValueParentIdStrings -ValueRow $v
        $raw = $v.value; if ($null -eq $raw) { $raw = $v.Value }
        if ($raw -isnot [string] -or -not $raw) { continue }
        if ($parentStrs -contains $NameSlot) { $fileName = $raw }
        if ($parentStrs -contains $MimeSlot) { $mime = $raw }
    }
    if (-not $fileName) {
        foreach ($v in @($vals)) {
            $raw = $v.value; if ($null -eq $raw) { $raw = $v.Value }
            if ($raw -is [string] -and ($raw.EndsWith(".exe") -or $raw.EndsWith(".apk"))) {
                $fileName = $raw
                break
            }
        }
    }
    if (-not $fileName) { return $null }
    return @{ FileName = $fileName; ContentType = $mime }
}

function Get-PlatformChildIdSet {
    param([string] $PlatformId, [string] $Token, [string] $Base)
    $platList = Invoke-Json -Method POST -Url ($Base + "api/Entities/GetEntities?fromCache=false") -Token $Token -Body @{
        parentIds = @($PlatformId)
        skip = 0
        take = 500
        includeValues = $false
        showHidden = $true
    }
    $set = New-Object System.Collections.Generic.HashSet[string]
    foreach ($p in @($platList)) {
        $id = [string]$p.id; if (-not $id) { $id = [string]$p.Id }
        if ($id) { [void]$set.Add($id) }
    }
    return $set
}

function Find-HistoryArchiveForSemver {
    param(
        [string] $HistoryId,
        [string] $PlatformId,
        [string] $Semver,
        [string] $Token,
        [string] $Base
    )
    $list = Invoke-Json -Method POST -Url ($Base + "api/Entities/GetEntities?fromCache=false") -Token $Token -Body @{
        parentIds = @($HistoryId)
        skip = 0
        take = 500
        includeValues = $true
        showHidden = $true
    }
    $onPlatform = Get-PlatformChildIdSet -PlatformId $PlatformId -Token $Token -Base $Base
    foreach ($item in @($list)) {
        $code = $item.code; if (-not $code) { $code = $item.Code }
        if ($code -ne "CloudFile") { continue }
        $id = [string]$item.id; if (-not $id) { $id = [string]$item.Id }
        if (-not $id -or -not $onPlatform.Contains($id)) { continue }
        $meta = Get-CloudFileLeafAndMime -EntityId $id -Token $Token -Base $Base
        if (-not $meta) { continue }
        $parsed = Get-SemverFromArtifactFileName -FileName $meta.FileName
        if ($parsed -and ($parsed -eq $Semver)) { return $id }
    }
    return $null
}

function Get-HistoryArchiveRowsForPlatform {
    param(
        [string] $HistoryId,
        [string] $PlatformId,
        [string] $Token,
        [string] $Base
    )
    $list = Invoke-Json -Method POST -Url ($Base + "api/Entities/GetEntities?fromCache=false") -Token $Token -Body @{
        parentIds = @($HistoryId)
        skip = 0
        take = 500
        includeValues = $true
        showHidden = $true
    }
    $onPlatform = Get-PlatformChildIdSet -PlatformId $PlatformId -Token $Token -Base $Base
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($item in @($list)) {
        $code = $item.code; if (-not $code) { $code = $item.Code }
        if ($code -ne "CloudFile") { continue }
        $id = [string]$item.id; if (-not $id) { $id = [string]$item.Id }
        if (-not $id -or -not $onPlatform.Contains($id)) { continue }
        $meta = Get-CloudFileLeafAndMime -EntityId $id -Token $Token -Base $Base
        $fileName = ""
        if ($meta -and $meta.FileName) { $fileName = [string]$meta.FileName }
        $semver = Get-SemverFromArtifactFileName -FileName $fileName
        if (-not $semver) { $semver = $fileName }
        $created = $item.creationTime
        if (-not $created) { $created = $item.CreationTime }
        if (-not $created) { $created = [datetime]::MinValue }
        $createdUtc = ([datetime]$created).ToUniversalTime().ToString("o")
        $row = New-Object psobject -Property @{
            id = $id
            semver = $semver
            creationTimeUtc = $createdUtc
        }
        [void]$rows.Add($row)
    }
    return $rows
}

function Copy-CloudBlobObject {
    param(
        [string] $SourceEntityId,
        [string] $DestEntityId,
        [string] $ContentType
    )
    if (-not (Test-Path $BlobUploader)) { throw "Missing $BlobUploader" }
    & dotnet run --project $BlobUploader -c Release --no-launch-profile -- `
        --key $DestEntityId `
        --copy-from $SourceEntityId `
        --content-type $ContentType `
        --endpoint $MinioEndpoint `
        --access-key $MinioAccessKey `
        --secret-key $MinioSecretKey `
        --bucket $MinioBucket
    if ($LASTEXITCODE -ne 0) { throw "CloudBlobUpload --copy-from failed with exit code $LASTEXITCODE" }
}

function New-HistoryArchiveCloudFile {
    param(
        [string] $HistoryId,
        [string] $PlatformId,
        [string] $FileName,
        [string] $ContentType,
        [string] $Token,
        [string] $Base,
        [string] $Description = ""
    )
    $body = @{
        name = $FileName
        description = $Description
        code = "CloudFile"
        codeIsUnique = $false
        order = 0
        hidden = $false
        parentIds = @($HistoryId)
        values = @(
            @{
                type = 0
                order = 0
                hidden = $false
                parentIds = @($MimeSlot)
                value = $ContentType
            },
            @{
                type = 0
                order = 1
                hidden = $false
                parentIds = @($NameSlot)
                value = $FileName
            }
        )
        permissions = @(@{
            id = [guid]::Empty
            permissionForId = $PublicId
            canWrite = $false
        })
    }
    $resp = Invoke-Json -Method POST -Url ($Base + "api/Entities/AddEntity") -Token $Token -Body $body
    $id = [string]$resp.id
    if (-not $id) { $id = [string]$resp.Id }
    if (-not $id) { throw "AddEntity CloudFile archive returned no id" }
    Invoke-Json -Method POST -Url ($Base + "api/Entities/AddParent") -Token $Token -Body @{
        id = $id
        parentId = $PlatformId
    } | Out-Null
    return $id
}

function Remove-HistoryArchiveCloudFile {
    param(
        [string] $EntityId,
        [string] $Token,
        [string] $Base
    )
    try {
        Invoke-Json -Method POST -Url ($Base + "api/Entities/RemovePermission") -Token $Token -Body @{
            entityId = $EntityId
            permissionForId = $PublicId
        } | Out-Null
    } catch {
        Write-Host "  warn: RemovePermission on archive $EntityId : $($_.Exception.Message)"
    }
    # DeleteEntity also deletes the MinIO object when code=CloudFile (FilesService).
    Invoke-Json -Method POST -Url ($Base + "api/Entities/DeleteEntity") -Token $Token -Body @{
        id = $EntityId
    } | Out-Null
    Write-Host "  pruned archive CloudFile $EntityId"
}

function Invoke-PrunePlatformHistory {
    param(
        [string] $HistoryId,
        [string] $PlatformId,
        [string] $PlatformLabel,
        [string] $Token,
        [string] $Base
    )
    $rows = @(Get-HistoryArchiveRowsForPlatform -HistoryId $HistoryId -PlatformId $PlatformId -Token $Token -Base $Base)
    if ($rows.Count -le $MaxArchivePerPlatform) {
        Write-Host "  history $PlatformLabel : $($rows.Count) archive row(s) (cap $MaxArchivePerPlatform) - no prune"
        return
    }
    $json = ($rows | ConvertTo-Json -Depth 5 -Compress)
    if ($rows.Count -eq 1) {
        # ConvertTo-Json unwraps single object; wrap as array for CLI.
        $json = "[$json]"
    }
    $pruneIds = Invoke-ArchiveRulesCli -CliArgs @("select-prune", "$MaxArchivePerPlatform", $json)
    foreach ($line in @($pruneIds)) {
        $pidToDelete = "$line".Trim()
        if (-not $pidToDelete) { continue }
        # Avoid $pid (PowerShell automatic variable).
        Remove-HistoryArchiveCloudFile -EntityId $pidToDelete -Token $Token -Base $Base
    }
    Write-Host "  history $PlatformLabel : pruned $($pruneIds.Count) oldest; target cap $MaxArchivePerPlatform"
}

function Invoke-SnapshotPreviousLatestToHistory {
    param(
        [string] $LatestCloudFileId,
        [string] $VersionEntityId,
        [string] $NewVersion,
        [string] $HistoryId,
        [string] $PlatformId,
        [string] $PlatformLabel,
        [string] $Token,
        [string] $Base,
        [string] $ContentTypeDefault
    )
    $currentVersion = Get-RemoteEntityNameSlotValue -EntityId $VersionEntityId -Token $Token -Base $Base
    if (-not (Test-ShouldSnapshotVersion -CurrentVersion "$currentVersion" -NewVersion $NewVersion)) {
        Write-Host "History $PlatformLabel : no snapshot (current='$currentVersion' new='$NewVersion')"
        return
    }

    $leaf = Get-CloudFileLeafAndMime -EntityId $LatestCloudFileId -Token $Token -Base $Base
    if (-not $leaf -or -not $leaf.FileName) {
        Write-Host "WARNING: History $PlatformLabel : semver change $currentVersion -> $NewVersion but latest CloudFile has no filename; skip snapshot"
        return
    }

    $archiveFileName = [string]$leaf.FileName
    $archiveSemver = Get-SemverFromArtifactFileName -FileName $archiveFileName
    if (-not $archiveSemver) {
        # Apply seed uses unversioned leaf; archive rows need a versioned name (D10).
        $archiveSemver = "$currentVersion".Trim()
        if ($PlatformLabel -eq "Windows") {
            $archiveFileName = "ProtoLink.Communicator.Windows-Setup-$archiveSemver.exe"
        } elseif ($PlatformLabel -eq "Android") {
            $archiveFileName = "ProtoLink.Communicator.Android-$archiveSemver.apk"
        }
        Write-Host "  latest leaf was unversioned; archive will use $archiveFileName"
    }

    $contentType = $leaf.ContentType
    if (-not $contentType) { $contentType = $ContentTypeDefault }

    $existing = Find-HistoryArchiveForSemver -HistoryId $HistoryId -PlatformId $PlatformId `
        -Semver $archiveSemver -Token $Token -Base $Base
    if ($existing) {
        Write-Host "History $PlatformLabel : reuse archive for $archiveSemver -> $existing (idempotent)"
        $archiveId = $existing
    } else {
        Write-Host "History $PlatformLabel : snapshot $currentVersion ($archiveFileName) -> new archive under history"
        $archiveId = New-HistoryArchiveCloudFile -HistoryId $HistoryId -PlatformId $PlatformId `
            -FileName $archiveFileName -ContentType $contentType -Token $Token -Base $Base `
            -Description "Archived from latest before $NewVersion"
    }

    Copy-CloudBlobObject -SourceEntityId $LatestCloudFileId -DestEntityId $archiveId -ContentType $contentType
    Set-CloudFileUploadMetadata -EntityId $archiveId -FileName $archiveFileName -ContentType $contentType `
        -Token $Token -Base $Base
    Write-Host "  archive blob ready: $($Base)api/Files/getFile/$archiveId/$archiveFileName"

    Invoke-PrunePlatformHistory -HistoryId $HistoryId -PlatformId $PlatformId `
        -PlatformLabel $PlatformLabel -Token $Token -Base $Base
}

function Publish-File {
    param(
        [string] $EntityId,
        [string] $Path,
        [string] $Token,
        [string] $Base,
        [string] $ContentType = "application/octet-stream"
    )
    $fileName = [System.IO.Path]::GetFileName($Path)
    Write-Host "Uploading $fileName -> entity $EntityId"

    # Prefer Files/addFile for small payloads; IIS often rejects ~50MB+ installers (413).
    $usedAddFile = $false
    $length = (Get-Item -LiteralPath $Path).Length
    if ($length -lt 20MB) {
        Add-Type -AssemblyName System.Net.Http
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $client = [System.Net.Http.HttpClient]::new($handler)
        $client.BaseAddress = [Uri]$Base
        $client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token)
        $multipart = [System.Net.Http.MultipartFormDataContent]::new()
        $multipart.Add([System.Net.Http.StringContent]::new($EntityId), "EntityId")
        $fs = [System.IO.File]::OpenRead($Path)
        try {
            $streamContent = [System.Net.Http.StreamContent]::new($fs)
            $streamContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
            $multipart.Add($streamContent, "File", $fileName)
            $resp = $client.PostAsync("api/Files/addFile", $multipart).GetAwaiter().GetResult()
            $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($resp.IsSuccessStatusCode) {
                Write-Host "  addFile ok: $body"
                $usedAddFile = $true
            } else {
                Write-Host "  addFile $($resp.StatusCode); falling back to MinIO PutObject (same key as FilesService)."
            }
        } finally {
            $fs.Dispose()
            $client.Dispose()
        }
    } else {
        Write-Host "  file is $([math]::Round($length/1MB,1)) MB - using MinIO PutObject (FilesService key = entity id)."
    }

    if (-not $usedAddFile) {
        if (-not (Test-Path $BlobUploader)) { throw "Missing $BlobUploader" }
        & dotnet run --project $BlobUploader -c Release --no-launch-profile -- `
            --key $EntityId `
            --file $Path `
            --content-type $ContentType `
            --endpoint $MinioEndpoint `
            --access-key $MinioAccessKey `
            --secret-key $MinioSecretKey `
            --bucket $MinioBucket
        if ($LASTEXITCODE -ne 0) { throw "CloudBlobUpload failed with exit code $LASTEXITCODE" }
    }

    # Both paths: force leaf name (+ mime). addFile alone leaves a prior display name stale.
    Set-CloudFileUploadMetadata -EntityId $EntityId -FileName $fileName -ContentType $ContentType -Token $Token -Base $Base
}

$windowsFileName = $null
$androidFileName = $null

# D10: snapshot previous latest into history (+ prune ≤5) before overwriting stable latest ids.
if ($uploadWindows) {
    Invoke-SnapshotPreviousLatestToHistory `
        -LatestCloudFileId $artifactWindowsId `
        -VersionEntityId $versionWindowsId `
        -NewVersion $windowsVersion `
        -HistoryId $releaseHistoryId `
        -PlatformId $platformWindowsId `
        -PlatformLabel "Windows" `
        -Token $token `
        -Base $BaseUrl `
        -ContentTypeDefault "application/octet-stream"
    Publish-File -EntityId $artifactWindowsId -Path $WindowsSetupPath -Token $token -Base $BaseUrl -ContentType "application/octet-stream"
    $windowsFileName = [System.IO.Path]::GetFileName($WindowsSetupPath)
}

if ($uploadAndroid) {
    Invoke-SnapshotPreviousLatestToHistory `
        -LatestCloudFileId $artifactAndroidId `
        -VersionEntityId $versionAndroidId `
        -NewVersion $androidVersion `
        -HistoryId $releaseHistoryId `
        -PlatformId $platformAndroidId `
        -PlatformLabel "Android" `
        -Token $token `
        -Base $BaseUrl `
        -ContentTypeDefault "application/vnd.android.package-archive"
    Publish-File -EntityId $artifactAndroidId -Path $AndroidApkPath -Token $token -Base $BaseUrl -ContentType "application/vnd.android.package-archive"
    $androidFileName = [System.IO.Path]::GetFileName($AndroidApkPath)
}

function Format-VersionEntityDescription {
    param($Meta)
    if (-not $Meta) { return "" }
    $lines = New-Object System.Collections.Generic.List[string]
    if ($null -ne $Meta.versionCode -and "$($Meta.versionCode)" -ne "") {
        [void]$lines.Add("versionCode=$($Meta.versionCode)")
    }
    if ($Meta.commit) { [void]$lines.Add("commit=$($Meta.commit)") }
    if ($Meta.buildDate) { [void]$lines.Add("buildDate=$($Meta.buildDate)") }
    return ($lines -join "`n")
}

function Set-PlatformVersionEntity {
    param(
        [string] $EntityId,
        [string] $PlatformLabel,
        [string] $VersionString,
        $Meta,
        [string] $Token,
        [string] $Base
    )
    # AddValue bumps entity version and stores only the values in that call at the new
    # version (GetEntities reads current version only) — so one AddValue of the name slot
    # replaces the previous visible version string (no stale duplicate on the page).
    # When description metadata is present, send name + description in one UpdateEntity
    # so both land on the same version (a second AddValue would drop the name).
    $desc = Format-VersionEntityDescription -Meta $Meta
    $nameValue = @{
        type = 0
        order = 0
        hidden = $false
        parentIds = @($NameSlot)
        value = $VersionString
    }
    if ($desc) {
        $ents = Invoke-Json -Method POST -Url ($Base + "api/Entities/GetEntities?fromCache=false") -Token $Token -Body @{
            ids = @($EntityId)
            skip = 0
            take = 1
            includeValues = $false
            showHidden = $true
        }
        $e = @($ents)[0]
        if (-not $e) { throw "Version entity $EntityId ($PlatformLabel) not readable before UpdateEntity" }
        $code = $e.code; if (-not $code) { $code = $e.Code }
        $order = 0
        if ($null -ne $e.order) { $order = [int]$e.order } elseif ($null -ne $e.Order) { $order = [int]$e.Order }
        $hidden = $false
        if ($null -ne $e.hidden) { $hidden = [bool]$e.hidden } elseif ($null -ne $e.Hidden) { $hidden = [bool]$e.Hidden } elseif ($null -ne $e.Hiddenn) { $hidden = [bool]$e.Hiddenn }
        $mainParent = $null
        if ($e.mainParentId) { $mainParent = [string]$e.mainParentId } elseif ($e.MainParentId) { $mainParent = [string]$e.MainParentId }
        $viewIds = ConvertTo-ViewIdList $e
        Invoke-Json -Method POST -Url ($Base + "api/Entities/UpdateEntity") -Token $Token -Body @{
            id = $EntityId
            name = $PlatformLabel
            description = ""
            code = $code
            order = $order
            hidden = $hidden
            mainParentId = $mainParent
            viewIds = $viewIds
            values = @(
                $nameValue,
                @{
                    type = 0
                    order = 1
                    hidden = $false
                    parentIds = @($DescSlot, $EnUs)
                    value = $desc
                }
            )
        } | Out-Null
        Write-Host "Wrote $PlatformLabel version $VersionString (+ description metadata) -> entity $EntityId"
    } else {
        Invoke-Json -Method POST -Url ($Base + "api/Entities/AddValue/$EntityId") -Token $Token -Body $nameValue | Out-Null
        Write-Host "Wrote $PlatformLabel version $VersionString -> entity $EntityId"
    }
}

function Set-ReleaseNotesIfAny {
    param(
        [string] $ReleaseEntityId,
        [string] $NotesText,
        [string] $Token,
        [string] $Base
    )
    if (-not $NotesText) { return }
    # Preserve the release name label ("Latest release"); only replace description.
    $ents = Invoke-Json -Method POST -Url ($Base + "api/Entities/GetEntities?fromCache=false") -Token $Token -Body @{
        ids = @($ReleaseEntityId)
        skip = 0
        take = 1
        includeValues = $true
        showHidden = $true
    }
    $e = @($ents)[0]
    if (-not $e) { throw "Release entity $ReleaseEntityId not readable before writing notes" }
    $code = $e.code; if (-not $code) { $code = $e.Code }
    $order = 0
    if ($null -ne $e.order) { $order = [int]$e.order } elseif ($null -ne $e.Order) { $order = [int]$e.Order }
    $hidden = $false
    if ($null -ne $e.hidden) { $hidden = [bool]$e.hidden } elseif ($null -ne $e.Hidden) { $hidden = [bool]$e.Hidden } elseif ($null -ne $e.Hiddenn) { $hidden = [bool]$e.Hiddenn }
    $mainParent = $null
    if ($e.mainParentId) { $mainParent = [string]$e.mainParentId } elseif ($e.MainParentId) { $mainParent = [string]$e.MainParentId }
    $viewIds = ConvertTo-ViewIdList $e

    $nameLabel = "Latest release"
    $vals = $e.values; if (-not $vals) { $vals = $e.Values }
    foreach ($v in @($vals)) {
        $parentStrs = Get-ValueParentIdStrings -ValueRow $v
        if ($parentStrs -contains $NameSlot) {
            $raw = $v.value; if ($null -eq $raw) { $raw = $v.Value }
            if ($raw -is [string] -and $raw) { $nameLabel = $raw; break }
        }
    }

    Invoke-Json -Method POST -Url ($Base + "api/Entities/UpdateEntity") -Token $Token -Body @{
        id = $ReleaseEntityId
        name = "Communicator release"
        description = ""
        code = $code
        order = $order
        hidden = $hidden
        mainParentId = $mainParent
        viewIds = $viewIds
        values = @(
            @{
                type = 0
                order = 0
                hidden = $false
                parentIds = @($NameSlot)
                value = $nameLabel
            },
            @{
                type = 0
                order = 1
                hidden = $false
                parentIds = @($DescSlot, $EnUs)
                value = $NotesText
            }
        )
    } | Out-Null
    Write-Host "Wrote release notes to description slot on $ReleaseEntityId (name left as '$nameLabel')"
}

if ($uploadWindows) {
    Set-PlatformVersionEntity -EntityId $versionWindowsId -PlatformLabel "Windows" `
        -VersionString $windowsVersion -Meta $windowsMeta -Token $token -Base $BaseUrl
} else {
    Write-Host "Windows version entity: left untouched"
}

if ($uploadAndroid) {
    Set-PlatformVersionEntity -EntityId $versionAndroidId -PlatformLabel "Android" `
        -VersionString $androidVersion -Meta $androidMeta -Token $token -Base $BaseUrl
} else {
    Write-Host "Android version entity: left untouched"
}

Set-ReleaseNotesIfAny -ReleaseEntityId $releaseId -NotesText $Notes -Token $token -Base $BaseUrl

Write-Host "Publish complete (no version written to release name slot)."
Write-Host "Download page: $($BaseUrl.TrimEnd('/'))/communicator"
if ($windowsFileName) {
    Write-Host "Windows file: $($BaseUrl)api/Files/getFile/$artifactWindowsId/$windowsFileName"
}
if ($androidFileName) {
    Write-Host "Android file: $($BaseUrl)api/Files/getFile/$artifactAndroidId/$androidFileName"
}

if ($SkipVerify) {
    Write-Host "WARNING: -SkipVerify set; anonymous gate D skipped. Do not treat this as a finished latest release."
} else {
    Write-Host "=== Gate D: name slot + anonymous verification ==="
    if ($uploadWindows) {
        Assert-CloudFileDisplayName `
            -BaseUrl $BaseUrl `
            -Token $token `
            -EntityId $artifactWindowsId `
            -ExpectedFileName $windowsFileName `
            -PlatformLabel "Windows"
    }
    if ($uploadAndroid) {
        Assert-CloudFileDisplayName `
            -BaseUrl $BaseUrl `
            -Token $token `
            -EntityId $artifactAndroidId `
            -ExpectedFileName $androidFileName `
            -PlatformLabel "Android"
    }

    Assert-AnonymousCommunicatorView -BaseUrl $BaseUrl

    if ($uploadWindows) {
        $commit = ""
        if ($windowsMeta -and $windowsMeta.commit) { $commit = [string]$windowsMeta.commit }
        $v = Assert-AnonymousFileMatchesLocal `
            -BaseUrl $BaseUrl `
            -EntityId $artifactWindowsId `
            -FileName $windowsFileName `
            -LocalPath $WindowsSetupPath `
            -PlatformLabel "Windows"
        Write-GateDStatusLine `
            -Platform "Windows" `
            -Version $windowsVersion `
            -Commit $commit `
            -Sha256 $v.Sha256 `
            -DownloadUrl $v.Url `
            -BaseUrl $BaseUrl | Out-Null
    }

    if ($uploadAndroid) {
        $commit = ""
        if ($androidMeta -and $androidMeta.commit) { $commit = [string]$androidMeta.commit }
        $v = Assert-AnonymousFileMatchesLocal `
            -BaseUrl $BaseUrl `
            -EntityId $artifactAndroidId `
            -FileName $androidFileName `
            -LocalPath $AndroidApkPath `
            -PlatformLabel "Android"
        Write-GateDStatusLine `
            -Platform "Android" `
            -Version $androidVersion `
            -Commit $commit `
            -Sha256 $v.Sha256 `
            -DownloadUrl $v.Url `
            -BaseUrl $BaseUrl | Out-Null
    }
}
