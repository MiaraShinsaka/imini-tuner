param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$version = "2.2.0"

if (-not $NoBuild) {
    & (Join-Path $root "build-tauri.ps1") -Edition full -OutDir "dist" -Name "iMini Tuner.exe"
    & (Join-Path $root "build-tauri.ps1") -Edition portable -OutDir "dist-portable" -Name "iMini Tuner.exe"
    & (Join-Path $root "build-tauri.ps1") -Edition lite -OutDir "dist" -Name "iMiniTuner-Lite.exe"
    & (Join-Path $root "sign-release.ps1") -Path @(
        (Join-Path $root "dist\iMini Tuner.exe"),
        (Join-Path $root "dist-portable\iMini Tuner.exe"),
        (Join-Path $root "dist\iMiniTuner-Lite.exe")
    )
    & (Join-Path $root "installer\build-installer.ps1") -NoBuild
}

# Sign the installer after ISCC has embedded the (already signed) app binary.
# Also runs with -NoBuild, where the existing installer is re-signed in place.
& (Join-Path $root "sign-release.ps1") -Path (Join-Path $root "dist\iMiniTuner-Setup.exe")

$dist = Join-Path $root "dist"
$setup = Join-Path $dist "iMiniTuner-Setup.exe"
$lite = Join-Path $dist "iMiniTuner-Lite.exe"
if (-not (Test-Path $setup)) { throw "missing installer: $setup" }
if (-not (Test-Path $lite)) { throw "missing lite build: $lite" }

$stage = Join-Path $root "build\portable-stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $root "dist-portable\iMini Tuner.exe") (Join-Path $stage "iMini Tuner.exe")
Copy-Item (Join-Path $root "show-window.ps1") (Join-Path $stage "show-window.ps1")
Copy-Item (Join-Path $root "show-window.cmd") (Join-Path $stage "show-window.cmd")
Copy-Item (Join-Path $root "installer\README-installed.md") (Join-Path $stage "README.md")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE")

$zip = Join-Path $dist "iMiniTuner-Portable.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

$release = Join-Path $root "release"
if (Test-Path $release) { Remove-Item -Recurse -Force $release }
New-Item -ItemType Directory -Force -Path (Join-Path $release "docs") | Out-Null

Copy-Item $setup (Join-Path $release "iMiniTuner-Setup.exe")
Copy-Item $lite (Join-Path $release "iMiniTuner-Lite.exe")
Copy-Item $zip (Join-Path $release "iMiniTuner-Portable.zip")
Copy-Item (Join-Path $root "README.md") (Join-Path $release "README.md")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $release "LICENSE")
Copy-Item (Join-Path $root "show-window.ps1") (Join-Path $release "show-window.ps1")
Copy-Item (Join-Path $root "show-window.cmd") (Join-Path $release "show-window.cmd")
Copy-Item (Join-Path $root "docs\*") (Join-Path $release "docs") -Recurse
$cert = Join-Path $root "certs\imini-tuner-selfsigned.cer"
if (Test-Path $cert) { Copy-Item $cert (Join-Path $release "iMiniTuner-SelfSigned.cer") }
$version | Set-Content (Join-Path $release "VERSION.txt") -Encoding ASCII

$files = @(
    (Join-Path $release "iMiniTuner-Setup.exe"),
    (Join-Path $release "iMiniTuner-Lite.exe"),
    (Join-Path $release "iMiniTuner-Portable.zip")
)
$lines = New-Object System.Collections.Generic.List[string]
foreach ($f in $files) {
    $h = Get-FileHash -Algorithm SHA256 -Path $f
    $lines.Add(($h.Hash.ToUpper() + "  " + (Split-Path $f -Leaf)))
}
$lines | Set-Content (Join-Path $release "SHA256SUMS.txt") -Encoding ASCII

Write-Host ""
Write-Host "Release artifacts:"
Get-ChildItem $release -File | Select-Object Name, Length | Format-Table -AutoSize
