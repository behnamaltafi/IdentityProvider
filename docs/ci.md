# CI validation (Jenkins setup deferred)

Prerequisites: .NET 10 SDK, supported Node.js (see package.json), npm, Git, and PowerShell 7 (`pwsh`). No Jenkins installation or configuration is part of this preparation. Existing NuGet package versions are preserved.

```powershell
npm ci
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1
npm run test:release
pwsh -NoProfile -File scripts/tests/Test-CommitMessages.ps1
pwsh -NoProfile -File scripts/tests/Test-OpenApiDiff.ps1
pwsh -NoProfile -File scripts/tests/Test-ImmutableReleaseTag.ps1
```

Use a full checkout. The build/test entry point independently validates commit history, restores, builds Release with deterministic CI settings, discovers solution test projects using evaluated IsTestProject properties, runs each project, requires fresh coverage reports, then repeats the application validator suite with an 80% line coverage threshold. Unique per-run result directories prevent stale coverage from passing a failed run. Native process exit codes are checked immediately. TRX and Cobertura files are actual test output under artifacts/test-results/<run-id>/. All source paths use the exact `Tests` casing and portable separators. No test failure is suppressed.

## Separate required OpenAPI check

Build flags alone cannot generate a document: the existing application uses Swashbuckle at runtime, rather than the Microsoft build-time OpenAPI generator. Preserve its behavior and perform a separate check against the running candidate API. This check is distinct from a successful build/test result. The CI script explicitly prints OpenAPI NOT CHECKED when no URL is supplied.

Start the freshly built/published candidate with deployment-safe test configuration, then run:

```powershell
# Run in a separate process/session and stop it after validation.
dotnet IdentityProvider.Api/bin/Release/net10.0/IdentityProvider.Api.dll --urls http://127.0.0.1:5087
# On master (initial contract):
pwsh -File scripts/openapi/Test-OpenApiContract.ps1 -ApiBaseUrl http://127.0.0.1:5087
# For subsequent candidate builds, supply the actual retained master artifact:
pwsh -File scripts/openapi/Test-OpenApiContract.ps1 -ApiBaseUrl http://127.0.0.1:5087 -MasterOpenApiBaseline <path-to-retained-master-contract>
```

The check requests /health/live and /swagger/v1/swagger.json, validates the returned document and writes artifacts/openapi/IdentityProvider.Api.json. No placeholder contract is substituted. The comparator rejects the structural breaking changes it understands; it is not a complete OpenAPI compatibility engine (for example, schema combinators and all response semantics are not exhaustively modeled). Retain the real successful master artifact; bootstrap it from master rather than fabricating a snapshot. No controllers currently exist, so a genuinely generated contract may have an empty paths object.

Alternatively pass `-OpenApiBaseUrl <url> -MasterOpenApiBaseline <path>` to Invoke-CI.ps1. Non-master builds using that integrated option require the retained master baseline. The earlier -MainOpenApiBaseline name remains an alias for compatibility, but stable policy is master.

## Linux agents and containers

Run the same scripts with pwsh, npm ci and a supported .NET 10 SDK. Hooks are stored with LF endings and installed as executable on Unix. Docker uses the existing multi-stage non-root image; .dockerignore excludes machine state, secrets and build/npm outputs. Build with `docker build -t identityprovider:ci .` where a Docker daemon is available. The Windows local bare remote path must be made reachable to a future Linux checkout. Linux execution and a completed Docker build must be verified on those agents; source portability alone is not a claim that these environments have run.

Before the first approved commit only, `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/ci/Invoke-CI.ps1 -WorkingTreeValidation` runs build/test/coverage and explicitly reports commit-history validation as pending. This option refuses to run once HEAD exists. Normal CI never opts into it. On Windows machines requiring signed scripts, use `-ExecutionPolicy Bypass` for this reviewed process only; this does not change the machine execution policy.
