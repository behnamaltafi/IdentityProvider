[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$OutputPath,
    [string]$BuildId = $(if ($env:BUILD_ID) { $env:BUILD_ID } elseif ($env:BUILD_NUMBER) { $env:BUILD_NUMBER } else { 'local' })
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$commit = (git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve HEAD.' }

$manifest = [ordered]@{
    service = 'IdentityProvider'
    version = $Version
    gitTag = "v$Version"
    commit = $commit
    buildId = $BuildId
    releasedAt = [DateTime]::UtcNow.ToString('o')
    openApiContract = 'openapi/IdentityProvider.Api.json'
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$json = $manifest | ConvertTo-Json
[System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
