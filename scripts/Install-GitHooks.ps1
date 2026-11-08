[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $root 'node_modules/@commitlint/cli/cli.js'))) { throw 'Run npm ci before installing hooks.' }
git -C $root config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Could not configure core.hooksPath.' }
if ($IsLinux -or $IsMacOS) {
    chmod +x (Join-Path $root '.githooks/commit-msg')
    if ($LASTEXITCODE -ne 0) { throw 'Could not make hook executable.' }
}
Write-Host 'Git hooks enabled from .githooks.'
