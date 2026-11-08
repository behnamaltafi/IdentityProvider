[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$before = @(git -C $root tag --list)
foreach ($name in @('Initialize-VersionBaseline.ps1','Publish-ProductionRelease.ps1')) {
    $rejected = $false
    try { & (Join-Path $root "scripts/ci/$name") }
    catch { $rejected = $_.Exception.Message -match 'retired|disabled' }
    if (-not $rejected) { throw "$name did not reject publication." }
}
$after = @(git -C $root tag --list)
if (@(Compare-Object $before $after).Count -gt 0) { throw 'Tag set changed.' }
Write-Host '[PASS] Legacy publication/baseline entry points refuse mutation; tags unchanged.'
