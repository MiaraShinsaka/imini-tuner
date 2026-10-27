param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (-not $NoBuild) {
    & (Join-Path $root "build-tauri.ps1") -Edition full -OutDir "dist" -Name "iMini Tuner.exe"
    if ($LASTEXITCODE -ne 0) { throw "app build failed" }
}

$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $iscc = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) {
    $iscc = Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) { throw "ISCC.exe not found. Install Inno Setup 6 first." }

Write-Host "Compiling installer with $iscc ..."
& $iscc "/Qp" (Join-Path $PSScriptRoot "setup.iss")
if ($LASTEXITCODE -ne 0) { throw "installer compile failed" }

$out = Join-Path $root "dist\iMiniTuner-Setup.exe"
Get-Item $out | Select-Object FullName, Length, LastWriteTime | Format-List
