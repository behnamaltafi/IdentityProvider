[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9_-]*$')][string]$NewPlatformId,
    [Parameter(Mandatory)][string]$BackupDirectory,
    [string]$HelperImage = 'cicd-platform/agent:local'
)
$ErrorActionPreference = 'Stop'
$folder = [IO.Path]::GetFullPath($BackupDirectory)
$m = Get-Content (Join-Path $folder 'backup.json') -Raw | ConvertFrom-Json
if ($m.schema -ne 1 -or $m.file -ne 'nexus-data.tgz') { throw 'Unsupported Nexus backup manifest.' }
$archive = Join-Path $folder $m.file
if ((Get-FileHash $archive).Hash.ToLowerInvariant() -ne $m.sha256) { throw 'Nexus backup checksum mismatch.' }
$volume = "${NewPlatformId}_nexus-data"
docker volume inspect $volume *> $null
if ($LASTEXITCODE -eq 0) { throw 'Refusing to overwrite an existing volume. Use a new platform ID.' }
docker volume create $volume | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Volume creation failed.' }
$helper = 'nexus-restore-'+[Guid]::NewGuid().ToString('N')
try {
    docker create --name $helper --user 0 --memory 128m --cpus 1 --network none --mount "type=volume,source=$volume,target=/data" --entrypoint bash $HelperImage -ec 'test -z "$(ls -A /data)"; tar --numeric-owner -xzpf /tmp/nexus.tgz -C /data'
    if ($LASTEXITCODE -ne 0) { throw 'Restore helper creation failed.' }
    docker cp $archive "${helper}:/tmp/nexus.tgz"
    if ($LASTEXITCODE -ne 0) { throw 'Archive copy failed.' }
    docker start -a $helper
    if ($LASTEXITCODE -ne 0) { throw 'Nexus restore failed; volume retained for inspection.' }
} finally { docker rm $helper *> $null }
Write-Host 'Nexus data restored to a new volume. Start with the pinned compatible image; validate users, immutable policies, authenticated reads and release SHA256 inventories before cutover.'
