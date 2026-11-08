[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('ci','release','none')][string]$Mode,
    [Parameter(Mandatory)][string]$CIContainer,
    [Parameter(Mandatory)][string]$ReleaseContainer,
    [Parameter(Mandatory)][string]$JenkinsUrl,
    [Parameter(Mandatory)][string]$JenkinsAdminFile,
    [string]$CINode = 'ci-agent',
    [string]$ReleaseNode = 'release-agent'
)
$ErrorActionPreference = 'Stop'
$login = Get-Content -LiteralPath $JenkinsAdminFile -Raw | ConvertFrom-Json
$headers = @{Authorization='Basic '+[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($login.username):$($login.password)"))}
$nodes = Invoke-RestMethod ($JenkinsUrl.TrimEnd('/')+'/computer/api/json?tree=computer[displayName,executors[currentExecutable[url]],oneOffExecutors[currentExecutable[url]]]') -Headers $headers
if ($Mode -ne 'none') {
    $targetNode = if ($Mode -eq 'ci') { $CINode } else { $ReleaseNode }
    if (-not ($nodes.computer | Where-Object displayName -eq $targetNode)) { throw "Target Jenkins node not found: $targetNode" }
}
$stop = switch($Mode) { 'ci' { @(@{container=$ReleaseContainer;node=$ReleaseNode}) } 'release' { @(@{container=$CIContainer;node=$CINode}) } 'none' { @(@{container=$CIContainer;node=$CINode},@{container=$ReleaseContainer;node=$ReleaseNode}) } }
foreach ($item in $stop) {
    $node = $nodes.computer | Where-Object displayName -eq $item.node
    if (-not $node) {
        $active = docker inspect $item.container --format '{{.State.Running}}'
        if ($LASTEXITCODE -ne 0 -or $active -ne 'false') { throw "Cannot verify idle state for missing Jenkins node: $($item.node)" }
        continue
    }
    if (@($node.executors+$node.oneOffExecutors | Where-Object currentExecutable).Count) { throw "Node $($item.node) is busy; refusing to interrupt it." }
}
foreach ($item in $stop) {
    docker stop --timeout 30 $item.container | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Worker stop failed.' }
}
if ($Mode -ne 'none') {
    $start = if($Mode -eq 'ci'){$CIContainer}else{$ReleaseContainer}
    $memory = [long](docker inspect $start --format '{{.HostConfig.Memory}}')
    $running = @(docker ps -q)
    $total = 0L
    foreach ($id in $running) { $total += [long](docker inspect $id --format '{{.HostConfig.Memory}}') }
    $alreadyRunning = (docker inspect $start --format '{{.State.Running}}') -eq 'true'
    if (-not $alreadyRunning) { $total += $memory }
    $capacity = [long](docker info --format '{{.MemTotal}}')
    if ($memory -le 0 -or $total -gt $capacity) { throw 'Worker would exceed Docker memory capacity or lacks a finite memory limit.' }
    docker start $start | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Worker start failed.' }
}
Write-Host "Worker mode selected: $Mode. Opposite worker is stopped; no node or volume was deleted."
