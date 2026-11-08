# Explicit maintenance operation: resolve the complete dependency closure from Jenkins metadata.
# Review changes and rebuild/test before accepting this lock. Startup never resolves versions.
[CmdletBinding()]
param([version]$ControllerVersion = '2.580.2')
$ErrorActionPreference = 'Stop'
$platform = Split-Path $PSScriptRoot
$metadata = Invoke-RestMethod https://updates.jenkins.io/current/update-center.actual.json
$queue = [Collections.Generic.Queue[string]]::new()
Get-Content (Join-Path $platform 'controller/plugins.roots.txt') | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $queue.Enqueue($_) }
$pins = @{}
while ($queue.Count) {
    $name = $queue.Dequeue()
    if ($pins.ContainsKey($name)) { continue }
    $plugin = $metadata.plugins.$name
    if (-not $plugin -or [version]$plugin.requiredCore -gt $ControllerVersion) { throw "Plugin $name is unavailable/incompatible with pinned controller." }
    $pins[$name] = @{version=$plugin.version;sha256=$plugin.sha256;url=$plugin.url;requiredCore=$plugin.requiredCore}
    foreach ($dependency in $plugin.dependencies) { if (-not $dependency.optional) { $queue.Enqueue($dependency.name) } }
}
$lines = $pins.Keys | Sort-Object | ForEach-Object { "${_}:$($pins[$_].version)" }
[IO.File]::WriteAllLines((Join-Path $platform 'controller/plugins.txt'), $lines, [Text.UTF8Encoding]::new($false))
@{controller=$ControllerVersion.ToString();source='https://updates.jenkins.io/current/update-center.actual.json';plugins=$pins} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $platform 'controller/plugins.lock.json')
Write-Host "Locked $($pins.Count) plugins. Run image build, JCasC validation, Job DSL validation and restore rehearsal before rollout."
