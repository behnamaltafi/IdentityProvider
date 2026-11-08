[CmdletBinding()]
param([string]$From, [string]$To = 'HEAD', [string]$RepositoryPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checkout = if ($RepositoryPath) { $RepositoryPath } else { $root }
Push-Location $checkout
try {
    $cli = Join-Path $root 'node_modules/@commitlint/cli/cli.js'
    if (-not (Test-Path $cli)) { throw 'Run npm ci first.' }
    git rev-parse --verify $To *> $null
    if ($LASTEXITCODE -ne 0) { throw "Commit '$To' does not exist; commit validation requires history." }
    if ((git rev-parse --is-shallow-repository).Trim() -eq 'true') { throw 'Commit validation requires a full checkout. Fetch full history first.' }
    $range = if ($From) { "$From..$To" } else { $To }
    if ($From) {
        git rev-parse --verify $From *> $null
        if ($LASTEXITCODE -ne 0) { throw "Base commit '$From' does not exist." }
    }
    $commits = @(git rev-list --reverse $range)
    if ($LASTEXITCODE -ne 0 -or $commits.Count -eq 0) { throw 'No commits found in the requested validation range.' }
    foreach ($commit in $commits) {
        git show -s --format=%B $commit | node $cli --config (Join-Path $root 'commitlint.config.cjs') --strict
        if ($LASTEXITCODE -ne 0) { throw "Commit convention failed: $commit" }
    }
    Write-Host "Validated $($commits.Count) commit(s)."
}
finally { Pop-Location }
