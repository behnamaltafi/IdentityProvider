# Incremental GitHub CI adoption

The existing Jenkins controller can adopt GitHub CI before the full platform/data
migration. Its Jenkins home, users, historical jobs, Registry and application
deployments remain in place. No fresh bootstrap security realm is applied.

The adopted setup uses the `cicd-platform` Shared Library pinned to a full GitHub
commit, the project catalog in `infra/platform/projects`, and Job DSL in
`infra/platform/casc/jobs.groovy`. GitHub credentials are stored only in Jenkins.
The CI App reads source/PRs and writes commit status; the release App is separate.
Each credential's repository access strategy is explicitly limited to the project.

`infra/platform/adoption/compose.yaml` adds one isolated CI agent to an existing
controller. Copy its `.env.example` to a local non-secret env file, select unique
network/volume names, build the platform agent image, register the matching
one-executor Jenkins node, and store its inbound secret in an ignored local file.
Use PowerShell 7 or the same Docker CLI commands on Linux:

```text
docker compose --project-directory <lab-root> --env-file <local-env-file> -f <platform>/adoption/compose.yaml create
docker network connect --alias jenkins-controller <ci-network> <existing-controller-container>
docker compose --project-directory <lab-root> --env-file <local-env-file> -f <platform>/adoption/compose.yaml up -d
```

The controller joins the CI network while preserving its previous network. Do not
publish the agent or a new controller port. The agent has its own work volume, a
read-only public NuGet bootstrap feed and its inbound secret. It has no Docker
socket, release store or deployment environment mount. Future controller
recreation must reattach this network; the full platform Compose does this
declaratively and is the preferred destination after data migration.

Keep aggregate limits within the host budget. On the current lab, controller 2
GiB, two agents 2.25 GiB each, Registry 0.5 GiB and three application containers
0.25 GiB each total 7.75 GiB. Only one executor runs on each agent. The previous
agent was idle when its memory limit was reduced from 4 GiB to 2.25 GiB; its
Compose file was updated as well. Do not lower limits during a running build.

The basic branch build-strategy plugin is pinned alongside all other plugins.
`bootstrapHeads` records historical heads already present during adoption. Their
initial builds are skipped, but later revision changes build normally. New branch
names build on first discovery and origin PRs build their merge revision. Branch
discovery excludes branches with open PRs. Indexing is periodic; unchanged heads
must not create repeat builds. Fork PRs remain disabled.

New release/promote/rollback jobs stay disabled while `deploymentEnabled` is
false. Enabling them requires the trusted agent, environment paths, original
artifact inventories, Registry access and approvals to be validated first. This
does not disable or overwrite the retained legacy jobs and does not authorize an
official publication or Production deployment.

The first real master build exposed a Declarative parser conflict caused by a
method parameter named `options`, shadowing the `options {}` directive. Renaming
that parameter to `settings` fixes the parser conflict; Jenkins' actual Declarative
validator is used in addition to ordinary Groovy compilation. A successful source
parse alone must not be reported as a successful pipeline execution.

Live build, PR, indexing and isolation results are recorded separately in the lab
verification receipt. This adoption step is not a full Ubuntu or artifact migration.
