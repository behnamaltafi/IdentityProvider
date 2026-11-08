[CmdletBinding()]
param([string]$LabRoot = 'D:/Work/DevOpsLab')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
foreach ($name in @('internal','uat','production')) {
    $source = Join-Path $repository "deploy/environments/$name"
    $destination = Join-Path $LabRoot "environments/$name"
    New-Item -ItemType Directory -Force $destination | Out-Null
    $compose = Join-Path $destination 'compose.yml'
    if (Test-Path $compose) {
        if ((Get-FileHash $compose).Hash -ne (Get-FileHash (Join-Path $source 'compose.yml')).Hash) { throw "Existing $name Compose differs. Review it explicitly; no overwrite performed." }
    } else { Copy-Item (Join-Path $source 'compose.yml') $compose }
    $runtime = Join-Path $destination '.env.runtime'
    if (-not (Test-Path $runtime)) { Copy-Item (Join-Path $source '.env.example') $runtime }
    Write-Host "$name runtime configuration prepared without overwriting existing configuration."
}
