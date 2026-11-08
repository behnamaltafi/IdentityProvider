# Versioning and immutable releases

Stable versions are calculated exclusively by semantic-release on master. See the complete [Persian operations guide](CI-CD.fa.md) for lifecycle hooks, build-once packaging, persistent cumulative CHANGELOG, exact manifest/digest promotion, approval, rollback and recovery.

The private locked npm tooling includes commit analyzer, release-notes generator, changelog and exec. npm/GitHub publication and automatic Git changelog commits are disabled. release.config.cjs defaults to dryRun=true. The guarded scripts/release/run.mjs requires explicit approval, actual Jenkins build metadata, full history and exact remote master before allowing publication. Only semantic-release creates stable tags. The old manual tagging/publication PowerShell entry points remain disabled.

feat -> minor; fix/perf/revert -> patch; ! headers and breaking footers -> major. Other non-breaking types do not release. Matched revert pairs may be filtered by semantic-release; no second version calculator is used. CI identities are 0.0.0-ci.<SHA>, clearly disposable. Numeric assembly/file versions are X.Y.Z.0 and informational metadata records the full source SHA. Root deterministic props and inherited test configuration are retained.

Run npm ci, npm run test:release, npm run test:pipeline, and node scripts/tests/scenarios.mjs. The latter creates isolated fixture remotes/tags, never tags on the official lab origin. node scripts/release/preview-candidate.mjs reviews actual candidate commits on an independent master mirror without modifying real master. No official release is authorized by running tests or preview.
