$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
node Common/build/sync-launcher-site.mjs --check
if ($LASTEXITCODE) { exit $LASTEXITCODE }
node Common/build/sync-app-links.mjs --check
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet restore CPTools.slnx --locked-mode
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet build CPTools.slnx -c Release --no-restore
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet test CPTools.slnx -c Release --no-build --no-restore
if ($LASTEXITCODE) { exit $LASTEXITCODE }
node Common/build/test.mjs
exit $LASTEXITCODE
