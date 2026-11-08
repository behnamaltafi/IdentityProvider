# Disposable fixture rehearsal. Does not use or copy live Jenkins data or secrets.
[CmdletBinding()]
param([string]$HelperImage = 'cicd-platform/agent:migration-candidate')
$ErrorActionPreference = 'Stop'
$id = 'cicd-fixture-' + [Guid]::NewGuid().ToString('N').Substring(0,12)
$restored = "$id-restored"
$output = Join-Path ([IO.Path]::GetTempPath()) $id
$names = @('jenkins-home','registry-data','artifacts','release-work','ci-work')
foreach ($name in $names) {
    $volume = "${id}_$name"
    docker volume create --label cicd.test.fixture=true $volume | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Fixture creation failed.' }
    docker run --rm --user 0 --mount "type=volume,source=$volume,target=/data" --entrypoint bash $HelperImage -ec 'printf retained-fixture > /data/probe; chmod 640 /data/probe; chown 1001:1001 /data/probe'
    if ($LASTEXITCODE -ne 0) { throw 'Fixture seeding failed.' }
}
& (Join-Path $PSScriptRoot 'Backup-Platform.ps1') -PlatformId $id -Destination $output -HelperImage $HelperImage
& (Join-Path $PSScriptRoot 'Restore-Platform.ps1') -NewPlatformId $restored -BackupDirectory $output -HelperImage $HelperImage
foreach ($name in $names) {
    docker run --rm --user 0 --mount "type=volume,source=${restored}_$name,target=/data,readonly" --entrypoint bash $HelperImage -ec 'test "$(cat /data/probe)" = retained-fixture; test "$(stat -c %a /data/probe)" = 640; test "$(stat -c %u:%g /data/probe)" = 1001:1001'
    if ($LASTEXITCODE -ne 0) { throw "Fixture restore validation failed: $name" }
}
$blocked = $false
try { & (Join-Path $PSScriptRoot 'Restore-Platform.ps1') -NewPlatformId $restored -BackupDirectory $output -HelperImage $HelperImage } catch { $blocked = $true }
if (-not $blocked) { throw 'Restore overwrote an existing destination.' }
Write-Host "PASS five-volume fixture backup/restore: content, mode, ownership and overwrite rejection. Fixture volumes retained: $id and $restored; backup: $output"
