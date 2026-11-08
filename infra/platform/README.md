# Portable Jenkins platform

Start with [the migration runbook](../../docs/platform-migration.md). This is a
separate candidate platform; it does not replace or restart the existing lab.

Set `.env` from `.env.example`, use a reviewed GitHub library commit, provision
secrets locally, run `pwsh -File scripts/Get-Plugins.ps1`, then validate and build.
Do not apply the bootstrap security realm over an existing Jenkins home.

The controller has no Docker socket or executors. The CI agent has no release
storage or socket. The trusted release agent owns deployment privileges. This
is for trusted repositories, with administrator-only job configuration by default.
