[CmdletBinding()]
param([string]$LabRoot = 'D:/Work/DevOpsLab')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$snapshot = Join-Path $LabRoot 'artifacts/audit/jenkins-update-center.json'
if (-not (Test-Path $snapshot)) { throw 'Preserve a compatible official stable update-center snapshot before resolving plugins.' }
$metadata = Get-Content $snapshot -Raw | ConvertFrom-Json
$pending = [Collections.Generic.Queue[string]]::new()
foreach ($name in @('git','workflow-aggregator','credentials-binding','pipeline-input-step','junit','coverage','lockable-resources','timestamper')) { $pending.Enqueue($name) }
$plugins = @{}
while ($pending.Count) {
    $name = $pending.Dequeue()
    if ($plugins.ContainsKey($name)) { continue }
    $plugin = $metadata.plugins.$name
    if (-not $plugin) { throw "Plugin $name absent from preserved metadata." }
    $plugins[$name] = $plugin
    foreach ($dependency in $plugin.dependencies) { if (-not $dependency.optional) { $pending.Enqueue($dependency.name) } }
}
$controller = Join-Path $LabRoot 'jenkins/controller'
$lines = $plugins.Keys | Sort-Object | ForEach-Object { "${_}:$($plugins[$_].version)" }
[IO.File]::WriteAllText((Join-Path $controller 'plugins.txt'), ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$lock = @{jenkinsCore=$metadata.core.version;plugins=@($plugins.Keys | Sort-Object | ForEach-Object { @{id=$_;version=$plugins[$_].version;sha256=$plugins[$_].sha256;url=$plugins[$_].url;requiredCore=$plugins[$_].requiredCore} })}
$lock | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8NoBOM (Join-Path $controller 'plugins.lock.json')
Copy-Item (Join-Path $repository 'jenkins/lab/10-jobs.groovy') (Join-Path $controller '10-jobs.groovy')
$dockerfile = Join-Path $controller 'Dockerfile'
$content = [IO.File]::ReadAllText($dockerfile)
if (-not $content.Contains('10-jobs.groovy')) {
    $content += "COPY --chown=jenkins:jenkins 10-jobs.groovy /usr/share/jenkins/ref/init.groovy.d/10-jobs.groovy`n"
    [IO.File]::WriteAllText($dockerfile, $content, [Text.UTF8Encoding]::new($false))
}
Write-Host "Pinned $($plugins.Count) required plugins and additive job seed. Build/recreate only the controller after reviewing running jobs."
