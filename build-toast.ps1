$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$outDir = Join-Path $root "src-tauri\resources"
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir "imini-toast.exe"
$sources = @(
    (Join-Path $root "src\Theme.cs"),
    (Join-Path $root "src\Native.cs"),
    (Join-Path $root "src\ToastForm.cs"),
    (Join-Path $root "toast\ToastProgram.cs")
)
& $csc /nologo /target:winexe /platform:x64 /optimize+ /out:$out `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0) { throw "通知窗口编译失败" }
Write-Host "已生成 $out"