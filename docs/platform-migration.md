# GitHub and portable CI/CD platform migration

Status: **GitHub source migration approved; infrastructure cutover pending**. The running `bonak-lab` and its repositories,
volumes, credentials, tags and release artifacts are retained. Do not load the fresh
platform security configuration into the existing controller. The platform source
is `infra/platform`; the deployable staging copy is `D:/Work/DevOpsLab/portable-platform`.
Historical local-lab guides describe the previous architecture; this document is
the migration entry point. No remote change, push, GitHub Release publication or
real credential change is authorized by preparation alone.

## Inspection and required changes

| Existing component | New configuration |
| --- | --- |
| `origin` pointing at `D:/Work/DevOpsLab/git/IdentityProvider.git` | Authoritative `https://github.com/behnamaltafi/IdentityProvider.git`, after approval and ref verification; preserve bare repository and `legacy-local` remote |
| Lab Compose and local Git mounts | Separate portable Compose, no Git mounts or `ALLOW_LOCAL_CHECKOUT`; parameterized ports and persistent named volumes |
| Controller init Groovy and manual job setup | JCasC and per-project Job DSL; never replace the live security realm during preparation |
| Existing plugin list | Complete dependency lock with official SHA256 values, pinned controller/agent base-image digests |
| Four application Jenkinsfiles | Thin calls to a pinned, reviewed Jenkins Shared Library |
| Application names and paths in orchestration | `cicd/project.json`; separate administrator-owned `projects/*.json` for job generation |
| Local-only semantic-release | Required GitHub release plugin, automated tags/notes/ZIP/manifest/changelog assets; no npm publication |
| Branch CI using privileged agent | Dedicated CI agent without Docker socket, release artifacts or environment secrets |
| Host-specific environment directories | Portable `/lab/environments/<project>/<environment>`; configuration snapshots are hashed with each approval |

The supplied GitHub repository was read anonymously during preparation: public,
default-branch metadata `main`, size zero, and no advertised Git refs. This is a
point-in-time observation; recheck before pushing. Do not assume it remains empty.
Existing local `master`, `develop`, feature/release/hotfix branches and `v1.0.0`,
`v1.0.1` must be preserved. Do not create replacement version tags manually.

## Separation of concerns

* `infra/platform/compose.yaml`, `controller`, `agent`, `casc`: reusable infrastructure.
* `infra/platform/library/vars/dotnetPipeline.groovy`: orchestration with no application names.
* `cicd/project.json`: application paths, artifact namespace, image repository, environment order and adapter commands.
* `infra/platform/projects/*.json`: trusted GitHub job catalog; one entry per application.
* `environments/<project>/<environment>/compose.yml` and `.env.runtime`: separately supplied deployment config and runtime secrets. These are not baked into images.
* Jenkins Credentials and ignored `secrets/`: authentication and inbound-agent secrets.
* `artifacts`, `registry-data`, `jenkins-home`: separate persistent data volumes. Agent workspaces are independent volumes.

The Shared Library starts from this repository's `infra/platform/library` subtree.
It can later move to its own reviewed GitHub repository by changing library URL,
path and pinned commit. `allowVersionOverride: false` prevents application branches
from selecting a different trusted library version. Only administrators may modify
the trusted library, catalog, JCasC, job definitions and release credentials.

## GitHub setup and approved Git migration

1. In GitHub, inspect `behnamaltafi/IdentityProvider`. If absent, create a repository
   without README, license or `.gitignore`; choose the intended visibility.
   If it has history, stop the empty-repository procedure and inspect ancestry in
   an isolated clone. Never force push, mirror-push, delete refs or overwrite history.
2. Review this working-tree diff and commit it on a reviewed Git Flow branch only
   when authorized. Integrate through develop and a release branch into master;
   retain existing branches. `ci:`/`build:`/`docs:` commits do not create versions.
3. Read-only migration plan (PowerShell 7 on either host):

   ```powershell
   pwsh -File infra/platform/scripts/Connect-GitHub.ps1
   git for-each-ref --format='%(objectname) %(refname)' refs/heads refs/tags
   git ls-remote --heads --tags https://github.com/behnamaltafi/IdentityProvider.git
   ```

4. **After explicit approval to change remotes**, run:

   ```powershell
   pwsh -File infra/platform/scripts/Connect-GitHub.ps1 -ApprovedRemoteChange
   git remote -v
   ```

   This adds `legacy-local` before changing `origin`; it never pushes. The bare
   repository and its protection hook remain intact.
5. **After separate approval to push the reviewed commits, branches and existing
   tags**, and only after confirming GitHub has no conflicting refs:

   ```powershell
   git push --atomic origin --all
   git push --atomic origin --tags
   pwsh -File infra/platform/scripts/Verify-GitHub.ps1
   ```

   These two commands are individually atomic, not a single joint transaction.
   Recheck all refs afterward; if either fails, inspect before retrying. A fresh
   full-history clone must have matching source commits, tags and trees. Do not
   remove the local bare repository after verification; keep it as a retained backup.
6. GitHub currently uses `develop` as its default collaboration branch; the stable
   jobs explicitly select `master`. Changing the default is a separate administrator
   decision. Add branch rules requiring reviewed PRs
   and successful Jenkins CI on master/develop, disallow force pushes/deletion,
   and protect stable `v*` tags. Grant only the release App/token the narrowly
   required bypass for creating new release tags; do not grant tag deletion.
   Rule availability depends on repository visibility and GitHub plan.

Old tags migrate unchanged. This does **not** create GitHub Release records for
1.0.0/1.0.1. Importing those historical GitHub Release records requires a separate
approval and must upload the retained original bytes without rebuilding or retagging.
Future releases are created exclusively by semantic-release.

## Credentials: private repositories and least privilege

Prefer two GitHub Apps installed only on selected repositories:

* Discovery/checkout App: Metadata read, Contents read, Pull requests read, Commit
  statuses write if Jenkins publishes statuses. Add Checks write only if checks
  integration is used. Use the App credential in GitHub Branch Source and for
  HTTPS checkout; test both private API discovery and actual clone.
* Release App: Metadata read and Contents write for tag pushes and GitHub Releases.
  Issue/PR comments and labels are disabled in semantic-release. Additional API
  access should be granted only when a failing, reviewed operation requires it.

In Jenkins Credentials, create `platform-github-read`,
`identityprovider-github-read` and `identityprovider-github-release` after approval. GitHub
App credentials hold the App ID and PEM private key; install only on the required
repositories. Jenkins GitHub App credentials can supply installation tokens to
Git credential and username/password bindings; validate this on the pinned plugins.
The library binds release authentication only around preview/publication and
rebinds after input to obtain a fresh token. Never log tokens or use them in URLs.

Fallback: fine-grained PAT limited to the selected repository, with the same
permissions and an expiry. Store as Jenkins **Username with password**, username
the real GitHub account, password the token. Do not use a Secret Text credential
for the current release binding. Store it in the trusted application's credential
scope; do not share write credentials with the CI multibranch job. For multiple
teams on a shared controller, configure folder-level authorization and use a
separate trusted release folder; an administrator-only fresh platform is the
provided default, not a multi-tenant security boundary.

Never paste passwords, App keys or tokens into chat, `.env`, source files or job
parameters. External secret files need restrictive Windows ACLs or Linux mode 600.
Backups of Jenkins home contain both encrypted credentials and decryption keys;
encrypt and restrict those backups as sensitive data.

## Bootstrap the separate portable platform

These steps are a controlled rollout procedure, not actions already completed.
Use PowerShell 7 (`pwsh`) on Windows and Ubuntu; install it on the Ubuntu host.

1. Place `infra/platform` at the desired infrastructure directory. Copy `.env.example`
   to `.env`. Choose a unique `PLATFORM_ID`, ports, and a reviewed **full commit SHA**
   for `LIBRARY_REVISION` available on GitHub. Do not use floating `master` for a
   trusted library. Defaults use ports 8180/5501 to avoid the existing 8080/5500 lab.
2. Run `scripts/Test-Host.ps1`. Require Linux containers, Docker Compose v2+, and
   roughly 8 GiB Docker memory. On Linux set `DOCKER_SOCKET_GID` from the host's
   socket group. Total platform service limits are 7 GiB; leave room for Docker
   and application deployments. Start one agent/workload at a time within the
   existing 8 GiB lab budget; do not run both complete platforms together.
3. Resolve binaries from the **existing** lock, not new versions:

   ```powershell
   pwsh -File scripts/Get-Plugins.ps1
   docker compose build
   ```

   `Get-Plugins` verifies every archive against the lock. The image repeats SHA256
   verification and never resolves transitive versions at startup. Refresh locks
   only via reviewed `Update-PluginLock.ps1`, followed by image and JCasC tests.
   OS packages in the agent's apt layer are not a frozen distribution snapshot;
   retain the built image by digest (or `docker image save`) for byte-identical
   recovery. Base image digests and tool images are pinned.
4. After credential/setup approval, provision `secrets/admin-password` locally.
   Start **controller only**: `docker compose up -d controller`. The fresh JCasC
   enables authentication, disables signup, grants admin only, creates no anonymous
   build permissions, enables zero controller executors and creates two agent nodes.
   The secret file is consumed using JCasC's file secret source.
5. Log in; add the approved GitHub credentials. Retrieve each inbound node's secret
   from Jenkins into its ignored local secret file without logging it. Restrict
   their ACLs. Supply per-project environment directories; then:

   ```powershell
   docker compose --profile ci up -d
   ```

6. Validate JCasC at Manage Jenkins → Configuration as Code → Check configuration.
   Inspect generated `${project}/branches`, `/release`, `/promote`, `/rollback` jobs.
   Verify the Shared Library URL, subtree and pinned commit. Validate the generated
   Job DSL against this controller's API before applying catalog changes.
7. Run read-only `scripts/Verify-Restore.ps1`, then the acceptance checks below.
   The current controller is not reconfigured automatically and its user records
   are not overwritten. For a restore, restore its original security state first;
   do not apply fresh bootstrap admin configuration over restored identities.

## Discovery, PR validation and release behavior

GitHub Branch Source indexes every five minutes. Indexing discovers branches/PRs
and compares revisions, scheduling builds only when the revision changes. Branch
discovery strategy 1 excludes branches with open PRs; origin PRs build the merge
revision, avoiding parallel branch/PR builds for the same open PR. Supported branch
names are master, develop, feature/*, bugfix/*, release/*, hotfix/*. Fork PR discovery
is off by default. Enable it only with reviewed trust policy and disposable isolated
agents; the long-lived CI agent has no Docker socket or release mounts but does
have an inbound-agent identity and a cached workspace, so it is not a hostile-code
sandbox. Repository access must remain limited to trusted collaborators.

SCM **change polling** belongs to individual jobs and checks an existing checkout.
Folder **indexing** enumerates GitHub heads and PR metadata, including new heads.
This platform uses indexing alone: no Jenkinsfile `pollSCM`, no stable-job polling,
no webhook assumption and no duplicate trigger for the same discovered revision.
An initial index builds newly discovered heads once, except historical heads
listed in the optional project catalog `bootstrapHeads`. Those heads build after
their next revision change or an explicit manual build. New branches still build
on first discovery, and origin PRs validate their merge revision immediately.
Application CI performs full
history checkout, commit validation, .NET build/tests and reports, without candidate
image pushes or semantic-release publication. Release jobs are separate trusted
master jobs, manually triggered with PUBLISH false by default.

The stable job previews, archives the plan, asks authorized users for publication
approval, and then semantic-release calculates the version, builds/tests once,
creates immutable payloads, pushes its tag and publishes GitHub release assets.
Internal/UAT/Production consume the manifest's same digest; Production has its
own human input bound to actor/build/operation/config/digest. Rollback selects a
retained previously healthy manifest, verifies compatibility and consumes the old
digest without build/publish/push. Existing immutable payloads are never edited.

During incremental adoption on the existing controller, the new deployment jobs
stay disabled until the project catalog explicitly sets `deploymentEnabled: true`
after trusted-agent, environment-root, Registry and artifact-store verification.
This switch does not approve publication or bypass the separate human inputs.

Publication is not an atomic transaction across Git, Registry, artifact storage
and GitHub Releases. If GitHub upload fails after the tag/storage finalize, stop
promotion, inspect retained data and reconcile the GitHub Release after approval.
Rerunning semantic-release will not republish an already-tagged commit. Do not
delete the tag or rebuild to repair an upload. The retained lifecycle `recover`
mode finalizes an interrupted local marker only; it does not repair GitHub uploads.

## Windows → Ubuntu migration and deployment hosts

Distinguish four independent work streams:

| Work stream | What moves | Verification |
| --- | --- | --- |
| Configuration portability | Compose, locked images/plugins, JCasC, catalog/library/project config | Linux host check, Compose config, JCasC and job validation |
| Data migration | Jenkins home/history/users and optional agent workspaces | Consistent backup/restore, controller starts, history exists, jobs/agents connect |
| Secrets migration | App private keys/PATs, inbound secrets, Jenkins master keys, runtime config | Secure transport, ACLs, actual private checkout and credential decryption |
| Artifact migration | ZIP/publish/manifest/checksum records and Registry blobs | Exact SHA256 inventories and Registry content digests, then running container digest |

1. Freeze release/deployment jobs; wait for running builds and approvals to finish
   or have the operator abort them. Record all Git refs, manifests, image digests,
   deployment/approval history and current environment config hashes.
2. Archive the versioned platform config and exact built images separately. Exclude
   `.env`, `secrets`, environment runtime files and binary plugin caches from source
   archives. Save built images by digest for offline recovery.
3. For the portable platform, stop it with `docker compose --profile ci --profile release stop`
   (never `down -v`). Run `Backup-Platform.ps1 -PlatformId <id> -Destination <new-dir>`.
   It refuses a running stack, backs up all five named volumes, retains ownership
   and produces a SHA256 inventory. Encrypt before transport; enforce host ACLs.
4. **Legacy lab data is different:** `bonak-lab-jenkins-home`,
   `bonak-lab-registry-data`, `bonak-lab-agent-work` use explicit names, and artifacts
   are a host directory. The portable backup helper intentionally does not pretend
   these are the same layout. After an approved maintenance stop, export those
   three volumes using the same tar/`docker cp` method and separately archive
   `D:/Work/DevOpsLab/artifacts`. Preserve `git`, `environments` and encrypted
   `secrets` separately. On Ubuntu restore to **new** volume names, including a new
   artifacts volume populated from that directory. Do not repoint the live lab.
   Reconcile legacy node names/job URLs/security settings manually against the new
   catalog; do not apply fresh security JCasC over the old home blindly.
5. Install Docker/Compose and PowerShell 7 on Ubuntu. Run compatibility checks;
   configure filesystem permissions, socket GID, DNS, firewall and disk capacity.
   Transfer encrypted archives over authenticated encrypted transport; verify hashes.
6. `Restore-Platform.ps1 -NewPlatformId <new-id> -BackupDirectory <dir>` restores
   only absent volume names. It refuses overwrites. Partial restores are retained
   for diagnosis; no data volume is silently deleted. Restore trusted archives only.
7. Move external secret files separately; set modes/ACLs. Preserve Jenkins
   `secrets/master.key` and `hudson.util.Secret` with its credential store. Start
   only one controller using those identities. Rotate/reissue agent and GitHub
   credentials after the approved migration and verify them again.
8. On the new server, run a TLS reverse proxy with authentication and restricted
   administration. Keep port 8080 private. Set `JENKINS_PUBLIC_URL` to the real HTTPS
   URL and configure proxy headers. In the approved GitHub App/webhook settings,
   set `https://<jenkins-host>/github-webhook/`, validate the shared secret, subscribe
   to push and pull-request events, and check GitHub delivery logs. Set folder
   indexing to a slow recovery interval, such as one day; remove redundant polling.
9. Run `Verify-Restore.ps1`, then private checkout, PR validation, no-release
   preview, release approval, artifact identity, environment smoke and rollback
   acceptance. A successful `docker compose up` is only a container startup check.
10. Cut traffic and enable release jobs only after validation and approval. Keep
    the Windows stack stopped but intact for recovery; never run competing
    controllers against the same home/Registry/artifact writable data.

## Registry relocation and retained digest identity

Back up Registry data consistently while writes are stopped; restore blobs and
manifests without converting media types. Configure authentication/TLS on a remote
Registry. `REGISTRY_API_URL` is the agent's HTTP API endpoint; `IMAGE_REPOSITORY`
in project config is the Docker daemon's routable repository address. They can
resolve differently on Docker Desktop but must identify the same Registry.

**Do not rewrite old manifests or SHA256SUMS** when changing endpoints. Retained
manifests contain `localhost:5500/...`, which means the new deployment daemon's
localhost. To keep these old artifacts deployable unchanged, provide a loopback
Registry/proxy on each deployment daemon at that address serving the original
repository bytes, or retain the original daemon for those releases. Prefer a
stable TLS DNS Registry name for all new releases. Simply editing `.env` does not
migrate the old manifests or make old digests accessible at a new authority.
If a different registry changes the manifest digest, do not claim identity; retain
the original content endpoint or plan a separately approved provenance migration.

For remote deployment use a preprovisioned authenticated Docker SSH/TLS context
on the trusted agent and set `DEPLOY_DOCKER_CONTEXT` per trusted job/environment.
The adapter uses it for pull, Compose and inspect. A single invocation targets
one context; for environments on different hosts, supply separately scoped job
configuration or extend the trusted adapter before rollout. The agent's registry
API must still validate the registry visible to the target daemon. Verify target
platform/architecture and port reachability; no daemon bind mount may point into
an agent-only workspace. Deploy configuration is copied as private Compose input.

## Add a second .NET project

1. Create a GitHub repository, Git Flow branches and Conventional Commit rules.
   Install read/release Apps only on that repository. Create distinct Jenkins
   credential IDs with folder scope and restrict job configuration permissions.
2. Copy the four thin Jenkinsfiles and application adapter scripts. Create
   `cicd/project.json` with a new id, application/ZIP names, solution and publish
   paths, test settings/priority test project/coverage threshold, and commands.
   Shared Library functions remain unchanged. An example is in
   `infra/platform/examples/second-project.json`.
3. Allocate unique artifact root, Docker image repository and Compose project
   prefix, plus a separate environment directory per application. Choose unique
   host ports and network names and the actual health/OpenAPI endpoints in each
   hashed environment Compose file. Give each project its own runtime secrets.
4. Add an administrator-reviewed `projects/<id>.json` catalog entry; reload JCasC
   after checking its Job DSL. Jobs are generated by project id. Review deletions
   separately; the helper does not delete repositories, volumes or Git refs.
5. Validate branch CI, private PR CI, master-only preview, docs-only no-release,
   one approved release, all digest promotions and approved rollback. Test two
   projects together to verify independent artifacts, locks, projects and ports.

## Acceptance checklist and evidence boundaries

The changed-file report is `docs/platform-changed-files.md`. Preparation checks
are recorded in `docs/platform-verification.md`; failures and unexecuted operations
must remain explicit. Required end-to-end evidence after approval:

* GitHub refs match every retained local branch/tag; fresh clone trees match.
* Private discovery/checkout and one origin PR merge build succeed on CI agent.
* Reindexing unchanged heads creates no repeat builds; stable jobs stay manual.
* New semantic-release creates exactly its calculated tag and GitHub Release with
  matching ZIP, manifest, inventory and cumulative changelog. No npm publication.
* Internal/UAT/Production run one identical approved manifest digest, with actual
  authorized human input before Production. Corruption and wrong-actor approvals fail.
* Rollback uses the retained digest with no build/publish/push and no modified files.
* A real backup/restore rehearsal preserves configuration, history, credentials,
  checksums and digests. Ubuntu verification is required before claiming Linux migration.

## Primary references

* [GitHub Branch Source and App authentication](https://plugins.jenkins.io/github-branch-source/)
* [JCasC configuration and secret sources](https://plugins.jenkins.io/configuration-as-code/)
* [Job DSL through JCasC](https://github.com/jenkinsci/job-dsl-plugin/wiki/JCasC)
* [semantic-release GitHub authentication, assets and permissions](https://github.com/semantic-release/github)
