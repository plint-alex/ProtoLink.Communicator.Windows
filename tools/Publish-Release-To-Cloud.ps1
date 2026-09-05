#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes Communicator installer/APK to ProtoLink cloud via Files API and updates release version values.

.DESCRIPTION
  Does not use MinIO access keys. Authenticates to ProtoLink.Api, resolves CloudFile entities
  (from communicator/ids.generated.json or by scanning release children), uploads via addFile,
  and sets communicator-release-latest name slot to -Version.

.EXAMPLE
  .\tools\Publish-Release-To-Cloud.ps1 -Version 1.0.1

.EXAMPLE
  .\tools\Publish-Release-To-Cloud.ps1 -Version 1.0.1 -AndroidApkPath C:\builds\app-release.apk
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $WindowsSetupPath = "",

    [string] $AndroidApkPath = "",

    [string] $Notes = "",

    [string] $BaseUrl = "http://protolink.ru/",

    [string] $Login = "admin",

    [string] $Password = "admin",

    [int] $GiveinPlaceId = 0,

    [string] $IdsFile = "",

    [string] $MinioEndpoint = "plintec.ru:9000",

    [string] $MinioAccessKey = "minioadmin",

    [string] $MinioSecretKey = "minioadmin",

    [string] $MinioBucket = "protolink-files"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $WindowsSetupPath) {
    $WindowsSetupPath = Join-Path $RepoRoot "artifacts\ProtoLink.Communicator.Windows-Setup.exe"
}
if (-not $IdsFile) {
    $IdsFile = "D:\- Projects\ProtoLink.ViewEditor\communicator\ids.generated.json"
}

$NameSlot = "00010003-0000-0000-0000-000000000000"
$DescSlot = "00010004-0000-0000-0000-000000000000"
$MimeSlot = "00010012-0000-0000-0000-000000000000"
$EnUs = "00010002-0002-0000-0000-000000000000"
$WindowsFileName = "ProtoLink.Communicator.Windows-Setup.exe"
$AndroidFileName = "ProtoLink.Communicator.Android.apk"
$BlobUploader = Join-Path $PSScriptRoot "CloudBlobUpload\CloudBlobUpload.csproj"

$BaseUrl = $BaseUrl.TrimEnd('/') + "/"

function Invoke-Json {
    param([string] $Method, [string] $Url, [object] $Body = $null, [string] $Token = "")
    $headers = @{ Accept = "application/json" }
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = $Url; Headers = $headers; ContentType = "application/json" }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress) }
    return Invoke-RestMethod @params
}

Write-Host "=== Publish Communicator release to ProtoLink cloud ==="
Write-Host "API: $BaseUrl"
Write-Host "Version: $Version"

if (-not (Test-Path $WindowsSetupPath)) {
    throw "Windows setup not found: $WindowsSetupPath"
}

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
$artifactWindowsId = $null
$artifactAndroidId = $null

if ($ids) {
    $releaseId = [string]$ids.release
    $artifactWindowsId = [string]$ids.artifactWindows
    $artifactAndroidId = [string]$ids.artifactAndroid
}

if (-not $releaseId) {
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
        if ($code -eq "communicator-release-latest") {
            $releaseId = [string]$c.id
            if (-not $releaseId) { $releaseId = [string]$c.Id }
        }
    }
}

if (-not $releaseId) { throw "communicator-release-latest not found" }

if (-not $artifactWindowsId -or -not $artifactAndroidId) {
    $rels = Invoke-Json -Method POST -Url ($BaseUrl + "api/Entities/GetEntities?fromCache=false") -Token $token -Body @{
        parentIds = @($releaseId)
        skip = 0
        take = 200
        includeValues = $true
        showHidden = $true
    }
    foreach ($c in @($rels)) {
        $code = $c.code; if (-not $code) { $code = $c.Code }
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
        $id = [string]$c.id; if (-not $id) { $id = [string]$c.Id }
        if ($fileName -eq $WindowsFileName) { $artifactWindowsId = $id }
        elseif ($fileName -eq $AndroidFileName) { $artifactAndroidId = $id }
    }
}

if (-not $artifactWindowsId) { throw "Windows CloudFile artifact not found under release" }

function Publish-File {
    param(
        [string] $EntityId,
        [string] $Path,
        [string] $Token,
        [string] $Base,
        [string] $ContentType = "application/octet-stream"
    )
    Write-Host "Uploading $(Split-Path $Path -Leaf) -> entity $EntityId"

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
            $fileName = [System.IO.Path]::GetFileName($Path)
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

        # Mirror addFile metadata: mimeType (+ keep filename name slot via AddValue)
        Invoke-Json -Method POST -Url ($Base + "api/Entities/AddValue/$EntityId") -Token $Token -Body @{
            type = 0
            order = 0
            hidden = $false
            parentIds = @($MimeSlot)
            value = $ContentType
        } | Out-Null
        $fileName = [System.IO.Path]::GetFileName($Path)
        Invoke-Json -Method POST -Url ($Base + "api/Entities/AddValue/$EntityId") -Token $Token -Body @{
            type = 0
            order = 1
            hidden = $false
            parentIds = @($NameSlot)
            value = $fileName
        } | Out-Null
        Write-Host "  mimeType/name values updated on entity"
    }
}

Publish-File -EntityId $artifactWindowsId -Path $WindowsSetupPath -Token $token -Base $BaseUrl -ContentType "application/octet-stream"

if ($AndroidApkPath) {
    if (-not (Test-Path $AndroidApkPath)) { throw "Android APK not found: $AndroidApkPath" }
    if (-not $artifactAndroidId) { throw "Android CloudFile artifact not found under release" }
    Publish-File -EntityId $artifactAndroidId -Path $AndroidApkPath -Token $token -Base $BaseUrl -ContentType "application/vnd.android.package-archive"
}

Write-Host "Updating release version values on $releaseId"
$values = @(
    @{
        type = 0
        order = 0
        hidden = $false
        parentIds = @($NameSlot)
        value = $Version
    }
)
if ($Notes) {
    $values += @{
        type = 0
        order = 1
        hidden = $false
        parentIds = @($DescSlot, $EnUs)
        value = $Notes
    }
}

# Prefer AddValuesInOneVersion via UpdateEntity merge pattern: AddValue each
foreach ($v in $values) {
    Invoke-Json -Method POST -Url ($BaseUrl + "api/Entities/AddValue/$releaseId") -Token $token -Body $v | Out-Null
}

Write-Host "Published $Version"
Write-Host "Download page: $($BaseUrl.TrimEnd('/'))/communicator"
Write-Host "Windows file: $($BaseUrl)api/Files/getFile/$artifactWindowsId/$WindowsFileName"
