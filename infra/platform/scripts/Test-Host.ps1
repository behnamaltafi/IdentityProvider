[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
docker info --format '{{.OSType}} {{.Architecture}} {{.MemTotal}}'
if ($LASTEXITCODE -ne 0) { throw 'Docker engine is unavailable.' }
$info = docker info --format '{{json .}}' | ConvertFrom-Json
if ($info.OSType -ne 'linux') { throw 'Linux containers are required on Windows and Linux.' }
if ($info.MemTotal -lt 7GB) { throw 'Allocate approximately 8 GiB to Docker before starting both agents.' }
docker compose version
if ($LASTEXITCODE -ne 0) { throw 'Docker Compose v2+ is required.' }
if ($IsLinux) {
    if (-not (Test-Path /var/run/docker.sock)) { throw 'Local Docker socket missing; configure a supported Docker context.' }
    stat -c 'DOCKER_SOCKET_GID=%g' /var/run/docker.sock
    Write-Host 'Use this GID in .env. Confirm UID 1001 can read deployment config, but not controller secrets.'
}
Write-Host 'Host compatibility passed. This does not validate data, credentials, artifacts or migration.'
