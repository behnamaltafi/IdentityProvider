[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $root
try {
    $cases = [ordered]@{
        'feat(api): add scoring' = $true
        'fix(answer): prevent duplicate answer' = $true
        'feat!: change contract' = $true
        'feat(api)!: change contract' = $true
        'perf: improve query' = $true
        'refactor(cache): simplify lookup' = $true
        'chore: update tooling' = $true
        'build: update SDK' = $true
        'migration: add database index' = $true
        'unknown: invalid type' = $false
        'update things' = $false
        'fix:' = $false
    }
    foreach ($case in $cases.GetEnumerator()) {
        $case.Key | node node_modules/@commitlint/cli/cli.js --strict
        if (($LASTEXITCODE -eq 0) -ne $case.Value) { throw "Unexpected commitlint result: $($case.Key)" }
        Write-Host "[PASS] $($case.Key)"
    }
}
finally { Pop-Location }
