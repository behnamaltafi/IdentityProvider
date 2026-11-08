[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9_-]*$')][string]$NewPlatformId,
    [Parameter(Mandatory)][string]$BackupDirectory,
    [string]$HelperImage = 'cicd-platform/agent:local'
)
$ErrorActionPreference = 'Stop'
$backupPath = [IO.Path]::GetFullPath($BackupDirectory)
$manifest = Get-Content (Join-Path $backupPath 'backup.json') -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.volumes.Count -ne 5) { throw 'Unsupported backup manifest.' }
$expected = @('jenkins-home','registry-data','artifacts','release-work','ci-work')
# Preflight every archive and destination before writing any volume.
foreach ($item in $manifest.volumes) {
    if ($item.name -notin $expected -or $item.file -ne "$($item.name).tgz") { throw 'Unsafe archive entry.' }
    if ((Get-FileHash (Join-Path $backupPath $item.file) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw 'Backup checksum mismatch.' }
    docker volume inspect "${NewPlatformId}_$($item.name)" *> $null
    if ($LASTEXITCODE -eq 0) { throw 'Restore requires entirely new volume names; refusing to overwrite existing data.' }
}
if (@($manifest.volumes.name | Sort-Object -Unique).Count -ne 5) { throw 'Duplicate/missing volume entry.' }
foreach ($item in $manifest.volumes) {
    $volume = "${NewPlatformId}_$($item.name)"
    docker volume create $volume | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Volume creation failed.' }
    $helper = "cicd-restore-$([Guid]::NewGuid().ToString('N'))"
    try {
        docker create --name $helper --user 0 --mount "type=volume,source=$volume,target=/data" --entrypoint bash $HelperImage -ec 'test -z "$(ls -A /data)"; tar --numeric-owner -xzpf /tmp/data.tgz -C /data'
        if ($LASTEXITCODE -ne 0) { throw 'Restore helper creation failed.' }
        docker cp (Join-Path $backupPath $item.file) "${helper}:/tmp/data.tgz"
        if ($LASTEXITCODE -ne 0) { throw 'Archive copy failed.' }
        docker start -a $helper
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $volume. Retained for inspection; no cleanup of data volumes." }
    } finally { docker rm $helper *> $null }
}
Write-Host 'Volume restore finished. Run restored-infrastructure verification before routing traffic or enabling releases.'
