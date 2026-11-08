param([switch]$SkipSetup)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    if (-not $SkipSetup) {
        foreach ($operation in @('--prepare-development', '--initialize-database', '--register-clients')) {
            & dotnet run --project IdentityProvider.Api -- $operation
            if ($LASTEXITCODE -ne 0) { throw "Development setup failed at $operation" }
        }
    }
    & dotnet run --project IdentityProvider.Api
    if ($LASTEXITCODE -ne 0) { throw 'IdentityProvider exited with an error' }
}
finally { Pop-Location }
