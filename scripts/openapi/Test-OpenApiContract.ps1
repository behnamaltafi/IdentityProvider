[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri]$ApiBaseUrl,
    [string]$MasterOpenApiBaseline,
    [string]$ArtifactsDirectory = 'artifacts'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifactRoot = if ([IO.Path]::IsPathRooted($ArtifactsDirectory)) { $ArtifactsDirectory } else { Join-Path $root $ArtifactsDirectory }
$base = $ApiBaseUrl.AbsoluteUri.TrimEnd('/')
$health = Invoke-WebRequest "$base/health/live" -TimeoutSec 30
if ($health.StatusCode -ne 200) { throw 'Candidate API health check failed.' }
$response = Invoke-WebRequest "$base/swagger/v1/swagger.json" -TimeoutSec 30
$contract = $response.Content | ConvertFrom-Json
if (-not $contract.openapi -or $null -eq $contract.paths -or -not $contract.info.title) { throw 'The API did not return a valid OpenAPI document.' }
$output = Join-Path $artifactRoot 'openapi/IdentityProvider.Api.json'
New-Item -ItemType Directory -Force (Split-Path -Parent $output) | Out-Null
[IO.File]::WriteAllText($output, $response.Content, [Text.UTF8Encoding]::new($false))
if ($MasterOpenApiBaseline) {
    # Isolate the legacy comparator's exit statement from this caller.
    & (Get-Process -Id $PID).Path -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot/Compare-OpenApi.ps1" `
        -Baseline $MasterOpenApiBaseline -Current $output -ReportPath (Join-Path $artifactRoot 'reports/openapi-diff.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Breaking OpenAPI changes detected.' }
}
Write-Host "Validated live OpenAPI artifact: $output"
