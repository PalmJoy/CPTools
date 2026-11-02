#!/bin/sh
set -eu
cd "$(dirname "$0")"
node Common/build/sync-launcher-site.mjs --check
node Common/build/sync-app-links.mjs --check
dotnet restore CPTools.slnx --locked-mode
dotnet build CPTools.slnx -c Release --no-restore
dotnet test CPTools.slnx -c Release --no-build --no-restore
node Common/build/test.mjs
