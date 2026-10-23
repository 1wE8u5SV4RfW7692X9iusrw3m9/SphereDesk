$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path (Join-Path $PSScriptRoot ".."))

dotnet restore SphereDesk.sln
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build SphereDesk.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test tests/SphereDesk.Core.Tests/SphereDesk.Core.Tests.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test tests/SphereDesk.Windows.Tests/SphereDesk.Windows.Tests.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish src/SphereDesk.App/SphereDesk.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$zip = "artifacts/SphereDesk-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "artifacts/win-x64/*" -DestinationPath $zip
Write-Host "Published $zip"
