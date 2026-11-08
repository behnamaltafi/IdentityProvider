# Verification report - 2026-11-06

Phases A-E are complete after explicit user approval of the initial commit. The repository is committed and both long-lived branches are pushed to the independent local bare remote. Docker and Linux-agent validation remain documented environment checks; Jenkins and actual publication were not started.

## Baseline before edits

- No .git directory or ancestor repository; no existing origin to replace. No AGENTS.md files found in the project or checked ancestors.
- Source, solution, project, props, tool manifests, Docker, scripts and test files inventoried with SHA-256 before modification. Full initial inventory: artifacts/audit/baseline-inventory.csv; focused source inventory: artifacts/audit/source-inventory.csv. Original source/tooling copies: artifacts/audit/original/ (ignored, local only).
- .NET SDK 10.0.400; runtime 10.0.11. Node 26.13.0; npm 11.20.0.
- Configured author: behna <behna@BEHNNAMPC>, retained exactly; no invented identity.
- `dotnet restore IdentityProvider.slnx`: exit 0.
- `dotnet build IdentityProvider.slnx -c Release`: exit 0, zero warnings/errors.
- `dotnet test IdentityProvider.slnx -c Release`: exit 0, 12 unit + 7 integration tests passed, zero failed/skipped.
- Logs: artifacts/audit/baseline-{restore,build,test}-full.log. Initial restricted-sandbox invocations exited 1 with empty/sparse diagnostics; normal-access reruns above succeeded. dotnet --info had an unrelated Service Control Manager access diagnostic under sandboxing.

## Verification of changes

| Check | Actual result |
| --- | --- |
| npm install --package-lock-only --ignore-scripts --no-audit --no-fund | Exit 0; exact direct versions and full lockfile generated |
| npm ci --no-audit --no-fund | Exit 0; 350 packages installed |
| npm run test:release | Exit 0; 15 analyzer cases passed, including ! headers, breaking footer and no-release types; no release engine/publication invoked |
| Test-CommitMessages.ps1 | Exit 0; 9 valid and 3 invalid message cases behave as expected |
| Actual shell commit-msg hook | Valid message exit 0, invalid message exit 3 |
| Install-GitHooks.ps1 | Exit 0; core.hooksPath=.githooks |
| Test-OpenApiDiff.ps1 | Exit 0; additive contract accepted and structural breaking change rejected |
| Test-ImmutableReleaseTag.ps1 | Exit 0; both old mutation entry points refuse execution, tag list unchanged |
| Invoke-CI.ps1 -WorkingTreeValidation | Exit 0; restore/build pass, zero warnings/errors, 12 unit + 7 integration tests pass; priority rerun 12 pass, coverage 90% >=80%; real TRX/Cobertura generated |
| Live Test-OpenApiContract.ps1 | Exit 0; health and Swagger endpoints fetched from temporarily running candidate; real OpenAPI 3.0.4 with title IdentityProvider.Api; output artifacts/openapi/IdentityProvider.Api.json |
| Test-Commits.ps1 before first commit | Expected exit 1 because HEAD did not yet exist; superseded by passing committed-history CI below |
| git fetch origin | Exit 0 with normal filesystem access |
| PowerShell parse check | All scripts parse successfully |
| Staged safety audit | 71 staged files scanned, zero credential-pattern matches and zero excluded local/generated paths; all existing .cs/.csproj SHA-256 values unchanged |
| git diff --cached --check | Existing trailing whitespace in unchanged ValidateBase64.cs:15 remains; extra EOF blank lines introduced in two edited scripts were corrected |
| Linux Docker build | Attempted; interrupted after approximately five minutes of slow Microsoft base-image downloads, before application build execution. Container build remains unverified; log artifacts/audit/docker-build.log |

The precommit build/test result explicitly excludes commit-history validation. The bootstrap switch is rejected once HEAD exists; regular CI always validates history. OpenAPI is explicitly separate unless a running API URL is passed. A fixed localhost port attempt failed with Windows socket error 10013; retry using an OS-assigned port succeeded and the temporary API process was stopped. Initial analyzer testing exposed ! parsing, fixed and retested; initial CI testing exposed VSTest duplicate attachment copies, fixed and retested. These were verification findings, not unresolved test failures.

Logs and generated evidence live under ignored artifacts/audit/ and artifacts/test-results/. No fabricated reports/contracts are supplied. The existing OpenAPI comparator covers a structural subset, as documented in ci.md.

## Safety and final Git state

- No application .cs or .csproj files changed. No application/test functionality removed; NuGet dependencies unchanged.
- Local appsettings.Local.json (empty connection value), IDE/user files, publish profiles, generated output, npm dependencies and audit archives are excluded. Required source/config templates and package-lock.json are included.
- Retired version configuration and old version-scenario tests moved intact into ignored audit storage. Original tooling is also backed up. No branch deletion, history rewrite, force-push, manual version tag or release publication occurred.
- Working repository initialized; master and develop exist locally, track origin/master and origin/develop, and are pushed to the bare remote. Current branch is master.
- Origin: D:/Work/DevOpsLab/git/IdentityProvider.git. Independent bare repository has symbolic HEAD refs/heads/master and both branch refs. Fetch succeeds.
- Initial implementation commit: 6e1edf9de35f22118b8779c2a338678855e29f48. A subsequent documentation-only commit records these verification results; obtain its exact hash with git rev-parse HEAD. Both long-lived branches are advanced together to that report commit without rewriting history. Tags: none.
- Approved initial commit message: `chore: prepare git flow and CI tooling`.
- Normal committed CI: `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/ci/Invoke-CI.ps1`, exit 0 at implementation commit 6e1edf9. Independent history validation passed; restore/build passed with zero warnings/errors; 12 unit and 7 integration tests passed; 12 priority tests passed with 90% line coverage. Version identity was 0.0.0-ci.6e1edf9de35f. Evidence: artifacts/audit/final-ci-committed.log; artifacts/test-results/30cc630be8334ae19b6014020f776ea8/. The subsequent report-only commit changes no build or test inputs.
- Release-rule tests, commitlint valid/invalid scenarios and disabled publication guards reran successfully after the initial commit. No release engine or publication was invoked.
- Remaining manual environment checks: complete Docker image download/build and validate PowerShell CI on a Linux agent. No outstanding Windows build/test failure. Jenkins configuration and a real release pipeline are deferred.
- Jenkins setup and real release pipeline remain outside scope. Linux pwsh execution must be validated on a Linux agent; Docker verification does not substitute for that.
