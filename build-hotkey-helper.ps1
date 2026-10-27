param()

# Builds the standalone shortcut-capture helper and copies it into the Tauri
# resources directory, where hotkey_capture.rs embeds it with include_bytes!.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$crate = Join-Path $root "hotkey-helper"
$resources = Join-Path $root "src-tauri\resources"

$cargo = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"
if (!(Test-Path $cargo)) {
    $found = Get-Command cargo.exe -ErrorAction SilentlyContinue
    if (-not $found) { throw "cargo.exe not found. Install Rust first (https://rustup.rs)." }
    $cargo = $found.Source
}

$programFilesX86 = ${env:ProgramFiles(x86)}
$vswhere = Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
$vcvars = $null
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vsPath) {
        $candidate = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
        if (Test-Path $candidate) { $vcvars = $candidate }
    }
}
if (-not $vcvars) {
    $candidate = Join-Path $programFilesX86 "Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
    if (Test-Path $candidate) { $vcvars = $candidate }
}
if (-not $vcvars) { throw "vcvars64.bat not found. Install the VS Build Tools C++ workload." }

$env:PATH = (Split-Path $cargo -Parent) + ";" + $env:PATH
$command = "call `"$vcvars`" >nul && cargo build --release --manifest-path `"$crate\Cargo.toml`" --bin imini-hotkey-helper"
& cmd.exe /c $command
if ($LASTEXITCODE -ne 0) { throw "hotkey helper build failed" }

$built = Join-Path $crate "target\release\imini-hotkey-helper.exe"
if (!(Test-Path $built)) { throw "build output missing: $built" }
New-Item -ItemType Directory -Force $resources | Out-Null
$target = Join-Path $resources "imini-hotkey.exe"
Copy-Item $built $target -Force
Write-Host "built $target"
