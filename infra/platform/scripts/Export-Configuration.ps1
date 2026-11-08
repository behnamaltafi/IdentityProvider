[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$platform = Split-Path $PSScriptRoot
$target = [IO.Path]::GetFullPath($Destination)
if (Test-Path $target) { throw 'Configuration export destination must be new.' }
New-Item -ItemType Directory $target | Out-Null
# Explicit allowlist: no .env, secret files, runtime config or plugin binary cache.
foreach ($name in @('compose.yaml','.env.example','README.md','casc','scripts','projects','examples','library','adoption','nexus')) {
    Copy-Item -LiteralPath (Join-Path $platform $name) -Destination (Join-Path $target $name) -Recurse
}
foreach ($name in @('controller','agent')) { New-Item -ItemType Directory (Join-Path $target $name) | Out-Null }
foreach ($name in @('Dockerfile','plugins.txt','plugins.roots.txt','plugins.lock.json','plugins.sha256')) {
    Copy-Item -LiteralPath (Join-Path $platform "controller/$name") -Destination (Join-Path $target "controller/$name")
}
foreach ($name in @('Dockerfile','entrypoint.sh')) { Copy-Item -LiteralPath (Join-Path $platform "agent/$name") -Destination (Join-Path $target "agent/$name") }
$documentationRoot = if (Test-Path (Join-Path $platform 'docs/platform-migration.md')) { Join-Path $platform 'docs' } else { Join-Path $platform '../../docs' }
New-Item -ItemType Directory (Join-Path $target 'docs') | Out-Null
Get-ChildItem -LiteralPath $documentationRoot -File | Where-Object { $_.Name -like 'platform-*.md' -or $_.Name -eq 'nexus-artifact-store.md' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target "docs/$($_.Name)") }
$readme = Join-Path $target 'README.md'
[IO.File]::WriteAllText($readme, ([IO.File]::ReadAllText($readme)).Replace('../../docs/platform-migration.md','docs/platform-migration.md'), [Text.UTF8Encoding]::new($false))
$inventory = Get-ChildItem -LiteralPath $target -Recurse -File -Force | ForEach-Object {
    @{file=$_.FullName.Substring($target.Length + 1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $target 'configuration-inventory.json')
Write-Host 'Versioned configuration exported. Data, external runtime configuration and secrets require separate migration.'
