[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9_-]*$')][string]$PlatformId,
    [Parameter(Mandatory)][string]$Destination,
    [string]$HelperImage = 'cicd-platform/agent:local'
)
$ErrorActionPreference = 'Stop'
if (@(docker ps -q --filter "label=com.docker.compose.project=$PlatformId").Count) { throw 'Freeze publication and stop Nexus before making a consistent data backup.' }
$volume = "${PlatformId}_nexus-data"
docker volume inspect $volume *> $null
if ($LASTEXITCODE -ne 0) { throw 'Nexus data volume not found.' }
$target = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $target) { throw 'Backup directory must be new.' }
New-Item -ItemType Directory $target | Out-Null
if ($IsLinux) { chmod 700 $target }
$helper = 'nexus-backup-'+[Guid]::NewGuid().ToString('N')
try {
    docker create --name $helper --user 0 --memory 128m --cpus 1 --network none --mount "type=volume,source=$volume,target=/data,readonly" --entrypoint bash $HelperImage -ec 'umask 077; tar --numeric-owner -czpf /tmp/nexus.tgz -C /data .'
    if ($LASTEXITCODE -ne 0) { throw 'Backup helper creation failed.' }
    docker start -a $helper
    if ($LASTEXITCODE -ne 0) { throw 'Nexus archive failed.' }
    docker cp "${helper}:/tmp/nexus.tgz" (Join-Path $target 'nexus-data.tgz')
    if ($LASTEXITCODE -ne 0) { throw 'Nexus archive copy failed.' }
} finally { docker rm $helper *> $null }
if ($IsLinux) { chmod 600 (Join-Path $target 'nexus-data.tgz') }
@{schema=1;volume=$volume;file='nexus-data.tgz';sha256=(Get-FileHash (Join-Path $target 'nexus-data.tgz')).Hash.ToLowerInvariant();createdAtUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $target 'backup.json')
Write-Host 'Nexus data backed up. Encrypt this archive: it contains the database, RBAC state and repository blobs. External Jenkins/service credential files migrate separately.'
