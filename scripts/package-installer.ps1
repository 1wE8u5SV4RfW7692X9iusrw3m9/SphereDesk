$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path (Join-Path $PSScriptRoot ".."))

& (Join-Path $PSScriptRoot "publish.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
if (-not (Test-Path -LiteralPath $iscc)) {
    throw "ISCC.exe was not found."
}

& $iscc (Join-Path $PSScriptRoot "SphereDesk.iss")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Join-Path (Get-Location) "artifacts\SphereDesk-Setup-1.0.0.exe"
if (-not (Test-Path -LiteralPath $setup)) {
    throw "Installer was not created."
}

Write-Host "Packed $setup"
