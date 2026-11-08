[CmdletBinding()]
param([string]$ComposeFile = (Join-Path $PSScriptRoot '../compose.yaml'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-Host.ps1')
docker compose -f $ComposeFile config --quiet
if ($LASTEXITCODE -ne 0) { throw 'Compose configuration failed.' }
docker compose -f $ComposeFile ps
docker compose -f $ComposeFile exec -T controller curl -fsS http://localhost:8080/login --output /dev/null
if ($LASTEXITCODE -ne 0) { throw 'Controller HTTP check failed.' }
docker compose -f $ComposeFile exec -T release-agent sh -ec 'dotnet --version; node --version; git --version; docker version --format "{{.Server.Version}}"; test -d /release-artifacts; test -d /lab/environments'
if ($LASTEXITCODE -ne 0) { throw 'Trusted agent dependency/mount check failed.' }
docker compose -f $ComposeFile exec -T ci-agent sh -ec 'test ! -S /var/run/docker.sock; ! grep -q " /release-artifacts " /proc/mounts; test ! -e /lab/environments; dotnet --version'
if ($LASTEXITCODE -ne 0) { throw 'CI isolation check failed.' }
Write-Host 'Container checks passed. Migration is incomplete until GitHub checkout/PR CI, credential decryption, retained ZIP inventories, registry digests, deployment health, human approval and rollback are verified.'
