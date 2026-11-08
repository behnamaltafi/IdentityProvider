[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9_-]*$')][string]$PlatformId,
    [Parameter(Mandatory)][string]$Destination,
    [string]$HelperImage = 'cicd-platform/agent:local'
)
$ErrorActionPreference = 'Stop'
# Quiesce jobs, stop this platform first. Never silently stop a running controller.
$running = @(docker ps -q --filter "label=com.docker.compose.project=$PlatformId")
if ($LASTEXITCODE -ne 0 -or $running.Count -gt 0) { throw 'Stop platform containers for a consistent backup first.' }
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path $destinationPath) { throw 'Choose a new backup directory; existing backups are never overwritten.' }
New-Item -ItemType Directory $destinationPath | Out-Null
if ($IsLinux) { chmod 700 $destinationPath }
$volumes = @('jenkins-home','registry-data','artifacts','release-work','ci-work')
$records = @()
foreach ($name in $volumes) {
    $volume = "${PlatformId}_$name"
    docker volume inspect $volume *> $null
    if ($LASTEXITCODE -ne 0) { throw "Missing required volume: $volume" }
    $helper = "cicd-backup-$([Guid]::NewGuid().ToString('N'))"
    try {
        docker create --name $helper --user 0 --mount "type=volume,source=$volume,target=/data,readonly" --entrypoint bash $HelperImage -ec 'umask 077; tar --numeric-owner -czpf /tmp/data.tgz -C /data .'
        if ($LASTEXITCODE -ne 0) { throw 'Backup helper creation failed.' }
        docker start -a $helper
        if ($LASTEXITCODE -ne 0) { throw "Backup failed: $volume" }
        $archive = Join-Path $destinationPath "$name.tgz"
        docker cp "${helper}:/tmp/data.tgz" $archive
        if ($LASTEXITCODE -ne 0) { throw 'Archive copy failed.' }
        if ($IsLinux) { chmod 600 $archive }
        $records += @{name=$name;volume=$volume;file="$name.tgz";sha256=(Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()}
    } finally { docker rm $helper *> $null }
}
@{schema=1;platform=$PlatformId;createdAtUtc=[DateTime]::UtcNow.ToString('o');volumes=$records} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $destinationPath 'backup.json')
Write-Host 'Data backup created. Jenkins home contains credential encryption keys. Encrypt the backup before transport; migrate external secrets separately.'
