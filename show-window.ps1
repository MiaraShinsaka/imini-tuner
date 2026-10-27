$ErrorActionPreference = "SilentlyContinue"

$eventName = "Local\iMiniTuner.Show"
$exe = Join-Path $PSScriptRoot "iMini Tuner.exe"
if (!(Test-Path $exe)) {
    $exe = Join-Path $env:ProgramFiles "iMini Tuner\iMini Tuner.exe"
}

# If the background process is already running, signal its named event.
try {
    $evt = [System.Threading.EventWaitHandle]::OpenExisting($eventName)
    $evt.Set() | Out-Null
    $evt.Dispose()
    Write-Host "已唤回 iMini Tuner。"
    exit 0
} catch {
    # The event does not exist, so the background process is not running.
}

if (!(Test-Path $exe)) {
    Write-Host "未找到 iMini Tuner.exe，请将本脚本放在程序目录中。" -ForegroundColor Yellow
    exit 1
}

Start-Process -FilePath $exe -ArgumentList "--show"
Write-Host "已启动 iMini Tuner。"
