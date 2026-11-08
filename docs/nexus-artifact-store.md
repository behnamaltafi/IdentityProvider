# Nexus release artifact store

The selected backend is **Sonatype Nexus Repository Community Edition 3.98.1**,
with the official image pinned by digest in `infra/platform/nexus/compose.yaml`.
This phase stores immutable ZIPs and release metadata in a per-project Raw hosted
repository. Existing Docker registries and all image digests remain unchanged;
moving container images or adding NuGet proxy repositories is a separate change.

## Verified local state (2026-11-08)

Nexus has been started at `http://localhost:8181` with a new persistent volume.
Docker Desktop's approved memory setting is 12288 MiB; the engine reports about
11.67 GiB usable memory. The idle legacy release agent is stopped, while the
isolated CI agent stays online. Jenkins, Registry and all three application
containers recovered healthy after Docker Desktop restarted.

Following explicit approval, the bootstrap admin password was rotated and saved
only in the local secrets directory. Two repository-scoped users and encrypted
Jenkins credentials were created. Anonymous access is disabled and the Raw hosted
repository `identityprovider-releases` enforces `ALLOW_ONCE`. Telemetry acknowledgement
was already true before provisioning.

Retained releases 1.0.0 and 1.0.1 were imported without rebuilding and restored
into an empty cache using the reader account. Both ZIP hashes and full extracted
inventories matched their originals. Repeated import reused identical assets.
The finalized catalog lists both versions. Actual HTTP checks returned 403 for
reader writes, 409 for publisher overwrites and 401 for anonymous reads. Only the
CI worker is left running. The legacy release Jenkins node is not registered;
new deployment jobs remain disabled pending the separate environment/Registry
migration. These artifact checks did not deploy an application or create a release.

## Storage and immutability

Each project gets a dedicated Raw hosted repository, for example
`identityprovider-releases`, configured with `storage.writePolicy=ALLOW_ONCE`. Its
publisher role can browse/read/add/edit that repository, but cannot delete assets
or administer repository policies. `edit` is needed for HTTP PUT; the deployment
policy still rejects overwriting existing assets. The consumer role has only
browse/read. Provisioning disables anonymous access. Jenkins receives scoped service
credentials, never the Nexus admin credential.

The repository contains:

```text
releases/<version>/packages/<application>-<version>.zip
releases/<version>/manifest.json
releases/<version>/SHA256SUMS
releases/<version>/CHANGELOG.md
releases/<version>/release-notes.md
releases/<version>/READY.json
published/<version>.json
```

The first five files are the original release bytes. `READY.json` records their
SHA256 values and provenance and is written only after all payload files have
been uploaded and read back successfully. The publication marker is written only
after the official Git tag has been verified by the release lifecycle. Interrupted
uploads can resume by verifying identical existing bytes; differing content is
rejected without overwrite. Partially uploaded prefixes cannot be deployed.

The ZIP already contains the exact publish directory. A cold consumer downloads
the canonical files, verifies their hashes, safely extracts the ZIP, verifies the
original complete SHA256SUMS inventory, and atomically exposes a read-only local
cache. It performs no .NET build, publish or Docker push. ZIP traversal/symlinks
are rejected. The current portable ZIP policy permits ASCII package entry names;
extend and test that policy before packaging Unicode-named static assets.

The local artifact root remains the release cache plus the independent deployment,
approval and lock journal. Those mutable journals still need their own backup.
The Nexus database/blob volume is the durable source for the immutable payloads
and published markers. Nexus failures do not cause a silent fallback to stale
local releases when `ARTIFACT_STORE_BACKEND=nexus` is enabled.

Immutability is enforced by policy, checksums and limited roles. Administrators can
still change policies; this is not hardware WORM storage. Protect admin access
and retain consistent, encrypted backups.

## Resources and one-agent operation

The lab uses the vendor-documented 2703 MiB heap and 2703 MiB direct-memory
settings, with a 6 GiB container limit. Nexus's documented minimum heap is 2703M
and minimum host memory is 8 GiB. Do not quietly lower those values to squeeze
the complete lab into the previous 8 GiB Docker budget.

With the approved 12 GiB setting, service caps are:

| Service | Cap |
| --- | --- |
| Nexus | 6 GiB |
| Jenkins controller | 2 GiB |
| One active agent | 2.25 GiB |
| Existing Registry | 0.5 GiB |
| Internal/UAT/Production | 0.75 GiB total |
| Total | 11.5 GiB |

The main platform now uses separate `ci` and `release` profiles. Do not activate
both at once. `Set-WorkerMode.ps1` checks Jenkins executors before stopping the
opposite worker, refuses to interrupt busy work, and checks finite memory limits
against Docker capacity before starting the selected worker. The script accepts
container/node names and local admin credential paths as parameters; it embeds
no machine-specific paths or credentials.

For an existing controller, start Nexus first and attach its artifact network to
the trusted release agent. The CI agent does not need artifact-store access. For
the full platform, use the main Compose together with
`nexus/agent-network.override.yaml` after creating the Nexus network. Do not merge
the standalone Nexus Compose's project-name/volume declarations into a running
controller project accidentally.

## Supported provisioning and required approval

`scripts/nexus/provision.mjs` uses supported REST APIs, not the disabled/end-of-life
Groovy Script API. It validates existing repositories, policies and roles before
reusing them and refuses to reset or overwrite existing service accounts.

```text
node scripts/nexus/provision.mjs plan NEXUS_URL ADMIN_JSON PROJECTS_JSON LOCAL_SECRETS_DIRECTORY
```

The project catalog is `infra/platform/nexus/projects.json`. Planned identities:

* `cicd-identityprovider-publish` → Jenkins `identityprovider-nexus-publish`.
* `cicd-identityprovider-read` → Jenkins `identityprovider-nexus-read`.

Only after explicit approval, run `apply` with
`NEXUS_CREDENTIAL_CHANGES_APPROVED=yes`. Optional bootstrap admin rotation uses
`NEXUS_ROTATE_BOOTSTRAP_ADMIN=yes`; the generated recovery credential is saved
locally before the server operation. Credential files use exclusive writes and
are never committed or printed. Store them in the ignored lab secrets directory
with restrictive Windows ACLs or Linux mode 600.

Nexus CE 3.97+ requires an administrator to acknowledge base telemetry. CE 3.98
has a 30-day telemetry-delivery grace period; no supported CE bypass exists.
The vendor describes anonymous usage/configuration statistics and excludes user
identifiers, credentials, repository/component names and paths. Review the
vendor policy and obtain the user's approval before acknowledging it. The
provisioner acknowledges only with the separately explicit
`NEXUS_ACKNOWLEDGE_REQUIRED_TELEMETRY=yes` flag; it is off by default. HTTP health
alone does not mean this onboarding requirement has been completed.

## Pipeline wiring and retained-release import

Per-project `artifactStore` configuration selects the Nexus endpoint/repository
and two Jenkins credential IDs. The Shared Library binds the publisher only
around official master publication. Promotion and rollback bind the reader.
Ordinary branch and PR CI do not receive Nexus credentials. Existing human
publication/Production approvals and GitHub tag guards remain in place.

The adapter is enabled explicitly by `ARTIFACT_STORE_BACKEND=nexus`. The client
uses `NEXUS_BASE_URL`, `NEXUS_REPOSITORY`, `NEXUS_USERNAME` and `NEXUS_PASSWORD`;
URLs cannot embed credentials, redirects are rejected, and remote endpoints must
use TLS. Local HTTP is restricted to loopback or the Docker service `nexus`.
Do not expose the loopback lab service publicly; use TLS and authentication for
a server deployment.

After provisioning/scoped credential validation, import each retained release:

```text
node scripts/artifacts/cli.mjs import 1.0.0
node scripts/artifacts/cli.mjs import 1.0.1
node scripts/artifacts/cli.mjs list
node scripts/artifacts/cli.mjs fetch 1.0.1
```

Run import in the trusted Linux tooling context with the existing release root
and scoped publisher credentials. It verifies the retained bytes against the
unchanged official GitHub tag and copies them without rebuilding, retagging or
changing old image repository/digest fields. For private GitHub repositories,
also provide a scoped Git credential through Jenkins. A separate empty local
artifact root and read-only Nexus credential must then reconstruct each version
and reproduce the original ZIP SHA256 and complete inventory.

Do not enable the new deployment jobs until this real Nexus integration, trusted
agent, Registry endpoint and per-project environment paths have been verified.
Do not publish a new version merely to test the adapter; version management
remains exclusively semantic-release on master.

## Backup, restore and host migration

Freeze publications, wait for active transfers, and stop Nexus for a consistent
database/blob snapshot. `Backup-Nexus.ps1` refuses a running Nexus project and
creates a tar archive with numeric ownership and a SHA256 manifest.
`Restore-Nexus.ps1` restores only to a new absent volume and refuses overwrites.
Both use bounded-memory, network-isolated helper containers. The data is owned by
Nexus UID 200; the fixture rehearsal verified UID/GID 200:200, mode and content.

Encrypt backups before transport: the database contains security state as well
as metadata. External Jenkins/service credential files are a separate secrets
migration. Restore with the pinned compatible image; check HTTP, authentication,
immutable policies, telemetry status and release SHA256 inventories before
cutover. Preserve the old deployment and artifact storage until that succeeds.

Changing Nexus's hostname does not require editing old release manifests: they
record the original Docker image authority/digest, not the Raw storage URL.
Changing the Docker Registry separately still requires preserving access to old
digest authorities or an explicitly reviewed image migration. A Compose startup
is not proof of a complete Ubuntu/data/secrets/artifact migration.

## Verification so far

* Official image digest, Nexus HTTP/container health, rotated admin authentication
  and scoped service authentication verified.
* 14 real-HTTP/ZIP contract tests passed on Linux, including interrupted uploads,
  read/write separation, immutable collisions, corruption, traversal/symlinks,
  cold-cache reconstruction and pagination loops.
* Existing 16 version-rule and 11 release/deployment policy tests passed.
* Updated Shared Library passed the live Jenkins Declarative validator.
* Backup/restore rehearsed only on new fixture volumes; preserved UID/GID 200:200,
  file mode/content, and rejected an existing destination.
* Actual immutable repository provisioning, scoped users, encrypted Jenkins
  credentials, retained-release imports and cold-cache restoration succeeded.
  Live access/immutability checks returned the expected 403/409/401 statuses.
* The live Raw API returns paths with a leading slash. Contract tests cover both
  path formats and the live finalized catalog was checked against both releases.

## Primary references

* [Official Docker image, persistence and JVM parameters](https://github.com/sonatype/docker-nexus3)
* [Nexus memory requirements](https://help.sonatype.com/en/nexus-repository-memory-overview.html)
* [Raw repositories](https://help.sonatype.com/en/raw-repositories.html)
* [Deployment policy and disable redeploy](https://help.sonatype.com/en/configurable-repository-fields.html)
* [Supported repositories REST API](https://help.sonatype.com/en/repositories-api.html)
* [Required telemetry and acknowledgement API](https://help.sonatype.com/en/nexus-repository-telemetry.html)
