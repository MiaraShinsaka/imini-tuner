param(
    [switch]$NoIcon,
    [switch]$Lite,
    [switch]$Portable,
    [string]$OutDir = "dist",
    [string]$Name = "iMini Tuner.exe"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "找不到 .NET Framework 4.x 的 csc.exe"
}

$dist = Join-Path $root $OutDir
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName }
$refs = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Management.dll"
) | ForEach-Object { "/r:$_" }

$manifest = Join-Path $root "app.manifest"
$outExe = Join-Path $dist $Name
$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/codepage:65001",
    "/win32manifest:$manifest",
    "/out:$outExe"
)
if ($Lite) { $cscArgs += "/define:LITE" }
if ($Portable) { $cscArgs += "/define:PORTABLE" }
$icon = Join-Path $root "assets\app.ico"
if (-not $NoIcon -and (Test-Path $icon)) {
    $cscArgs += "/win32icon:$icon"
}
$cscArgs += $refs
$cscArgs += $sources

Write-Host "Compiling with $csc ..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码 $LASTEXITCODE"
}

Write-Host "OK: $outExe"
Get-Item $outExe | Select-Object FullName, Length, LastWriteTime | Format-List
