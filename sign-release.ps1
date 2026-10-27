param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path
)

# Signs release binaries with the project's self-signed code-signing certificate.
# If the certificate is absent the build still succeeds, but the artifacts stay
# unsigned - that is intentional so a fresh checkout can build without secrets.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$pfx = Join-Path $root "certs\imini-tuner-selfsigned.pfx"
$passwordFile = Join-Path $root "certs\pfx-password.txt"

if (-not (Test-Path $pfx) -or -not (Test-Path $passwordFile)) {
    Write-Warning "signing certificate not found under certs\; artifacts will stay unsigned"
    return
}

$signtool = $null
$candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
    (Join-Path $env:ProgramFiles "Windows Kits\10\bin")
)
foreach ($base in $candidates) {
    if (-not (Test-Path $base)) { continue }
    $signtool = Get-ChildItem $base -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if ($signtool) { break }
}
if (-not $signtool) { throw "signtool.exe not found (install the Windows SDK signing tools)" }

$password = (Get-Content $passwordFile -Raw).Trim()

foreach ($file in $Path) {
    if (-not (Test-Path $file)) { throw "cannot sign missing file: $file" }
    & $signtool sign /f $pfx /p $password /fd SHA256 /d "iMini Tuner" $file
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed for $file" }
    & $signtool verify /pa $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signature verification failed for $file" }
    Write-Host "signed and verified: $file"
}
