[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$compare = Join-Path $repositoryRoot 'scripts/openapi/Compare-OpenApi.ps1'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("IdentityProvider-openapi-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

$baseline = '{"openapi":"3.1.0","paths":{"/answers":{"get":{"parameters":[],"responses":{"200":{"content":{"application/json":{"schema":{"type":"object","required":["id"],"properties":{"id":{"type":"integer"},"title":{"type":"string"}}}}}}}}}},"components":{"schemas":{}}}'
$additive = '{"openapi":"3.1.0","paths":{"/answers":{"get":{"parameters":[],"responses":{"200":{"content":{"application/json":{"schema":{"type":"object","required":["id"],"properties":{"id":{"type":"integer"},"title":{"type":"string"},"note":{"type":"string"}}}}}}}}},"/health":{"get":{"responses":{"200":{"description":"ok"}}}}},"components":{"schemas":{}}}'
$breaking = '{"openapi":"3.1.0","paths":{"/answers":{"get":{"parameters":[],"responses":{"200":{"content":{"application/json":{"schema":{"type":"object","required":["id"],"properties":{"id":{"type":"string"}}}}}}}}}},"components":{"schemas":{}}}'

try {
    $baselinePath = Join-Path $temporaryDirectory 'baseline.json'
    $additivePath = Join-Path $temporaryDirectory 'additive.json'
    $breakingPath = Join-Path $temporaryDirectory 'breaking.json'
    [System.IO.File]::WriteAllText($baselinePath, $baseline)
    [System.IO.File]::WriteAllText($additivePath, $additive)
    [System.IO.File]::WriteAllText($breakingPath, $breaking)

    & $compare -Baseline $baselinePath -Current $additivePath
    if ($LASTEXITCODE -ne 0) { throw 'Additive OpenAPI change was incorrectly rejected.' }
    & $compare -Baseline $baselinePath -Current $breakingPath
    if ($LASTEXITCODE -eq 0) { throw 'Breaking OpenAPI change was not rejected.' }
    Write-Host '[PASS] additive contract accepted; breaking contract rejected.'
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
}
