param(
    [ValidateSet("full", "portable", "lite")]
    [string]$Edition = "full",
    [string]$OutDir = "dist",
    [string]$Name = "iMini Tuner.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcTauri = Join-Path $root "src-tauri"

& (Join-Path $root "build-hotkey-helper.ps1")
if ($LASTEXITCODE -ne 0) { throw "快捷键录制助手编译失败" }
& (Join-Path $root "build-toast.ps1")
if ($LASTEXITCODE -ne 0) { throw "通知窗口编译失败" }
& (Join-Path $root "build-helper.ps1")
if ($LASTEXITCODE -ne 0) { throw "固件接口编译失败" }

$cargo = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"
if (!(Test-Path $cargo)) {
    $found = Get-Command cargo.exe -ErrorAction SilentlyContinue
    if (-not $found) { throw "未找到 cargo。请先安装 Rust（https://rustup.rs）。" }
    $cargo = $found.Source
}

$programFilesX86 = ${env:ProgramFiles(x86)}
$vswhere = "$programFilesX86\Microsoft Visual Studio\Installer\vswhere.exe"
$vcvars = $null
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vsPath) {
        $candidate = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
        if (Test-Path $candidate) { $vcvars = $candidate }
    }
}
if (-not $vcvars) {
    $candidate = "$programFilesX86\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
    if (Test-Path $candidate) { $vcvars = $candidate }
}
if (-not $vcvars) { throw "未找到 Visual Studio 的 vcvars64.bat。请安装 VS Build Tools 的 C++ 桌面开发组件。" }

$features = ""
if ($Edition -eq "portable") { $features = " --features portable" }
if ($Edition -eq "lite") { $features = " --features lite" }

$env:PATH = (Split-Path $cargo -Parent) + ";" + $env:PATH
Push-Location $srcTauri
try {
    $command = "call `"$vcvars`" >nul && cargo build --release$features"
    & cmd.exe /c $command
    if ($LASTEXITCODE -ne 0) { throw "cargo build 失败" }
} finally {
    Pop-Location
}

$built = Join-Path $srcTauri "target\release\imini-tuner.exe"
if (!(Test-Path $built)) { throw "未找到编译产物: $built" }
$outDirPath = Join-Path $root $OutDir
New-Item -ItemType Directory -Force $outDirPath | Out-Null
$target = Join-Path $outDirPath $Name
Copy-Item $built $target -Force
Write-Host "已生成 $target"
