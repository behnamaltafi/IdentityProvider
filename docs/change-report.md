# File change report - 2026-11-06

Compared against the SHA-256 source inventory captured before edits. All original files have local copies under ignored artifacts/audit/original/. Application source, project references, NuGet versions, Dockerfile and the deterministic props import chain are unchanged.

| File | Change |
| --- | --- |
| .gitignore | Exclude local secrets, environment files, private keys and CI/tool workspaces while retaining source and lockfiles |
| GitVersion.yml | Retired intact into ignored audit storage; replaced by semantic-release tooling |
| README.md | Link the new Git Flow, CI and versioning guides |
| .config/dotnet-tools.json | Remove the retired version-calculation tool dependency |
| .githooks/commit-msg | Replace regular-expression validation with locked local commitlint |
| docs/versioning.md | Document preview-only semantic-release and explicit build-version input |
| scripts/Install-GitHooks.ps1 | Require installed commitlint and make the hook executable on Unix |
| scripts/ci/Initialize-VersionBaseline.ps1 | Disable manual baseline tagging without mutating Git |
| scripts/ci/Invoke-CI.ps1 | Evaluate solution test projects, correct paths, preserve failure propagation and coverage, separate OpenAPI check |
| scripts/ci/Publish-ProductionRelease.ps1 | Disable publication pending a separately reviewed release pipeline |
| scripts/tests/Test-CommitMessages.ps1 | Exercise actual commitlint with valid and invalid message cases |
| scripts/tests/Test-GitVersion.ps1 | Retired intact into ignored audit storage; replaced by semantic-release tooling |
| scripts/tests/Test-ImmutableReleaseTag.ps1 | Verify retired mutation commands refuse execution and leave tag set unchanged |
| scripts/tests/Test-OpenApiDiff.ps1 | Use portable comparator path; preserve existing compatibility scenarios |
| scripts/versioning/Get-Version.ps1 | Use explicit release version input or non-release SHA identity without predicting releases |
| scripts/versioning/New-ReleaseNotes.ps1 | Include the root commit when no prior release tag exists |
| Tests/README.md | Document validator priority coverage and evaluated test discovery |
| .dockerignore | Added for repository preparation |
| .gitattributes | Added for repository preparation |
| Tests/coverlet.priority.runsettings | Added for repository preparation |
| commitlint.config.cjs | Added for repository preparation |
| docs/ci.md | Added for repository preparation |
| docs/git-flow.md | Added for repository preparation |
| docs/verification-report.md | Added for repository preparation |
| package-lock.json | Added for repository preparation |
| package.json | Added for repository preparation |
| release.config.cjs | Added for repository preparation |
| scripts/ci/Test-Commits.ps1 | Added for repository preparation |
| scripts/openapi/Test-OpenApiContract.ps1 | Added for repository preparation |
| scripts/tests/test-semantic-release.mjs | Added for repository preparation |
| docs/change-report.md | Added this complete file change inventory |

All other original files remain byte-for-byte unchanged. Git metadata initialized locally: master symbolic HEAD, local origin URL and core.hooksPath. The independent bare remote was newly created; no existing remote replaced. After user approval, initial commit 6e1edf9 was created and master/develop were pushed to origin. A documentation-only follow-up updates this report and verification-report.md with final Phase E results.
