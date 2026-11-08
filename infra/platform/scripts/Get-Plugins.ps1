[CmdletBinding()]
param([string]$CacheDirectory)
$ErrorActionPreference = 'Stop'
$platform = Split-Path $PSScriptRoot
$lock = Get-Content (Join-Path $platform 'controller/plugins.lock.json') -Raw | ConvertFrom-Json
$output = Join-Path $platform 'controller/plugins'
New-Item -ItemType Directory -Force $output | Out-Null
$checksums = @()
foreach ($property in ($lock.plugins.PSObject.Properties | Sort-Object Name)) {
    $name = $property.Name
    $p = $property.Value
    $hex = ([BitConverter]::ToString([Convert]::FromBase64String($p.sha256))).Replace('-','').ToLowerInvariant()
    $destination = Join-Path $output "$name.jpi"
    if (-not (Test-Path $destination) -and $CacheDirectory) {
        $cached = Join-Path $CacheDirectory "$name.jpi"
        if ((Test-Path $cached) -and (Get-FileHash $cached -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hex) { Copy-Item -LiteralPath $cached -Destination $destination }
    }
    if (-not (Test-Path $destination)) {
        Invoke-WebRequest -UseBasicParsing -Uri "https://archives.jenkins.io/plugins/$name/$($p.version)/$name.hpi" -OutFile $destination
    }
    if ((Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hex) { throw "Plugin $name hash mismatch; remove this invalid cached file and retry." }
    $checksums += "$hex  $name.jpi"
}
if (@(Get-ChildItem $output -Filter '*.jpi').Count -ne $checksums.Count) { throw 'Unexpected plugins in build context; review the cache.' }
[IO.File]::WriteAllLines((Join-Path $platform 'controller/plugins.sha256'), $checksums, [Text.UTF8Encoding]::new($false))
Write-Host "Verified $($checksums.Count) pinned plugins against official SHA256 values."
