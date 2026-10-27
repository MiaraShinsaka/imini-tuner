$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { throw "未找到 .NET Framework 4.x 的 csc.exe" }

$outDir = Join-Path $root "src-tauri\resources"
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir "imini-power.exe"
$sources = @(
    (Join-Path $root "src\PowerSwitch.cs"),
    (Join-Path $root "src\Log.cs"),
    (Join-Path $root "helper\HelperProgram.cs")
)

& $csc /nologo /target:exe /platform:x64 /optimize+ /out:$out `
    /r:System.dll /r:System.Core.dll /r:System.Management.dll $sources
if ($LASTEXITCODE -ne 0) { throw "固件接口编译失败" }
Write-Host "已生成 $out"
