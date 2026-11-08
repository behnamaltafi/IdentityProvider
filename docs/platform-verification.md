# Portable platform preparation verification

Preparation verified on Windows 11 using Docker Desktop's Linux engine. This is
not a completed GitHub cutover or an Ubuntu server migration. Validation was run
on 2026-11-06 and state was rechecked on 2026-11-07 (Asia/Tehran).

After the user approved source migration, another read-only check found all seven
original refs already present on GitHub at their baseline commits. `origin` already
pointed to GitHub and `legacy-local` preserved the original bare-repository URL.
The repository default branch was `develop`; it was left unchanged. The platform
changes are committed on a feature branch and integrated through develop and a
release branch into master using ordinary merge commits. This does not deploy
the new controller, provision credentials or publish a GitHub Release.

## Executed checks

| Check | Observed result |
| --- | --- |
| Initial repository inspection | Existing CI/CD implementation and releases retained; origin was local bare Git at the preparation baseline |
| Initial anonymous GitHub inspection | `behnamaltafi/IdentityProvider` existed and was public with no advertised heads/tags; subsequent approved migration check found all seven original refs matching the baseline |
| Plugin lock and archive verification | 84 plugins, complete required dependency closure, official SHA256 hashes verified |
| Candidate controller image | Built successfully; image ID `sha256:f2e0fbf3871671716f136c249978e52217a441b8d49af855a59c358ff220a834` |
| Candidate agent image | Built successfully; image ID `sha256:53068c7d822a1fa427faa8d07212dc57316c0b4a3e00636d15a28e358b65b187` |
| Disposable Jenkins initialization | Jenkins 2.580.2, JCasC loaded, all 84 plugins loaded, no failed plugins |
| Job DSL | Generated branches/release/promote/rollback jobs; actual GitHubSCMSource, origin PR merge discovery and branch strategy 1 verified |
| Groovy | Shared Library and four delegate bodies compiled using actual Jenkins plugin classpath; real GitHub library retrieval remains pending |
| Compose syntax | `docker compose config --quiet` passed with validation-only non-secret environment values |
| Host compatibility | Linux x86_64 engine, Compose 5.6.1; engine reported 10,381,656,064 bytes available memory; platform service limits total 7 GiB |
| Semantic rules | All 16 analyzer cases passed, including docs/ci/build no-release and breaking changes |
| Release/deployment policy tests | All 11 tests passed, including checksum corruption, registry digest verification, portable approval-variable compatibility, PR publication rejection, approval binding and rollback compatibility |
| Isolated real Git scenarios | First/patch semantic-release versions, cumulative changelog, docs-only and non-master no-release, hook-bypass commit rejection passed in temporary fixture repositories |
| Revised .NET CI adapter | Actual Release restore/build, 15 unit + 7 integration tests passed; 15 priority tests rerun, validator coverage 90% |
| Fixture backup/restore | All five volume kinds round-tripped known bytes, owner UID/GID 1001:1001 and mode 640; existing restore destinations were rejected |
| Preservation | Seven existing local branch/tag refs inventoried; no commits, remotes or official tags changed; retained release manifests/inventories hash checked |

The first plugin-installer build encountered refused mirror connections and fell
back to the official archive. The final build uses the complete checked-in lock
and separately downloaded checksum-verified binaries; startup resolves no versions.
The first disposable JCasC attempt exposed required orphan-retention DSL fields;
they were corrected and initialization passed. An attempted Windows fixture run
hit sandbox Git configuration permissions; the actual scenarios were rerun and
passed in the isolated Linux checkout. Network-related update-center diagnostics
in the disposable controller were expected because it had **no network**.

No credential from the live lab was read or changed. The disposable controller
used a random, generated test-only password, separate home, no host ports and no
network. It did not publish or reach GitHub. The existing controller was not
restarted or reconfigured by these validation commands.

## Evidence retained locally

* `artifacts/audit/github-migration-baseline.json`: unchanged origin, HEAD, five branches, two tags and release inventory hashes.
* `artifacts/audit/platform-controller-validation.json`: actual Jenkins/JCasC/plugin/job/Groovy result.
* `artifacts/audit/github-migration-dotnet.log`: actual .NET CI output.
* `artifacts/audit/github-migration-policy-final.log`: final 11 policy tests on Linux.
* `artifacts/audit/github-migration-scenarios-linux.log` and `github-migration-scenario-results.json`: isolated real Git/semantic-release results, explicitly not official releases.
* `infra/platform/controller/plugins.lock.json` and `plugins.sha256`: resolved dependency pins and verified binary hashes.

The staging export at `D:/Work/DevOpsLab/portable-platform` contains 30
configuration/documentation files plus its inventory. Every staged file hash was
checked. Final JCasC/Groovy initialization was rerun successfully on 2026-11-07;
the disposable controller was then stopped. Full checksum inventories for both
retained official releases also passed after preparation.

The disposable backup rehearsal retained fixture-only volumes with prefixes
`cicd-fixture-dcf138fe9d82` and `cicd-fixture-dcf138fe9d82-restored`, and the backup
at `/tmp/cicd-fixture-dcf138fe9d82` in the Linux agent. These contain only the
`retained-fixture` probe. They are not a backup of live Jenkins or release data.

## Source migration approval

The user approved committing the reviewed platform changes, keeping GitHub as
origin with the original origin retained as legacy-local, and pushing the preserved
branches and tags. All pushes must be ordinary fast-forward additions; no force
push, tag replacement or repository deletion is permitted. Run `Verify-GitHub.ps1`
after publication and verify a fresh clone's source tree.

## Remaining approval or external prerequisites

* Approve provisioning Jenkins GitHub App/PAT credentials and the platform rollout.
* Verify actual private checkout, GitHub PR build, library retrieval from its pinned
  reviewed commit, and unchanged-head indexing without repeat builds.
* Approve and execute a future semantic-release publication to verify GitHub
  Release assets; historical Release record imports need their own approval.
* Validate a real full-stack restore, secret decryption, Registry migration,
  remote-host health, approved promotions and rollback on the new architecture.
* Validate on an actual Ubuntu server. Linux containers on Windows do not prove
  Linux host ownership, socket groups, DNS, TLS, firewall or filesystem behavior.

OpenAPI runtime checks were not part of this build-only validation. No new
application deployment, GitHub Release or npm publication was performed.
