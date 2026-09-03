# Creates icon.ico from icon.png (ICO format with embedded PNG for Vista+)
$ProjectDir = $env:ProjectDir
if (-not $ProjectDir) { $ProjectDir = Split-Path -Parent $PSScriptRoot }
$AssetsDir = Join-Path $ProjectDir "Assets"
$pngPath = Join-Path $AssetsDir "icon.png"
$icoPath = Join-Path $AssetsDir "icon.ico"
if (-not (Test-Path $pngPath)) { exit 0 }
$png = [System.IO.File]::ReadAllBytes($pngPath)
$header = [byte[]]@(0,0,1,0,1,0)
$size = [System.BitConverter]::GetBytes([int]$png.Length)
$offset = [System.BitConverter]::GetBytes([int]22)
$dir = [byte[]]@(0,0,0,0,1,0,32,0) + $size + $offset
[System.IO.File]::WriteAllBytes($icoPath, $header + $dir + $png)
