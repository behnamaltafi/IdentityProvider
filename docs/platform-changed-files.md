# Changed files for the portable GitHub platform

This is the reviewed change inventory for the approved GitHub migration. The original five branches and two release tags were found already migrated with matching baseline commits; `origin` points to GitHub and `legacy-local` preserves the bare repository. Platform changes are integrated through Git Flow without rewriting history or creating release tags. Infrastructure rollout and credential provisioning remain separate steps.

## Modified files

- `.gitattributes`
- `.gitignore`
- `Jenkinsfile`
- `README.md`
- `docs/CI-CD.fa.md`
- `jenkins/Jenkinsfile.promote`
- `jenkins/Jenkinsfile.release`
- `jenkins/Jenkinsfile.rollback`
- `jenkins/flow.groovy`
- `jenkins/lab/10-jobs.groovy`
- `package-lock.json`
- `package.json`
- `release.config.cjs`
- `scripts/ci/Invoke-CI.ps1`
- `scripts/ci/package-candidate.mjs`
- `scripts/deploy/deploy.mjs`
- `scripts/release/context.cjs`
- `scripts/release/lib.mjs`
- `scripts/release/lifecycle.mjs`
- `scripts/release/preview-candidate.mjs`
- `scripts/release/run.mjs`
- `scripts/tests/pipeline.test.mjs`
- `scripts/tests/test-semantic-release.mjs`

## Added files

- `cicd/project.json`
- `docs/platform-changed-files.md`
- `docs/platform-migration.md`
- `docs/platform-verification.md`
- `infra/platform/.env.example`
- `infra/platform/agent/Dockerfile`
- `infra/platform/agent/entrypoint.sh`
- `infra/platform/casc/jenkins.yaml`
- `infra/platform/casc/jobs.groovy`
- `infra/platform/compose.yaml`
- `infra/platform/controller/Dockerfile`
- `infra/platform/controller/plugins.lock.json`
- `infra/platform/controller/plugins.roots.txt`
- `infra/platform/controller/plugins.sha256`
- `infra/platform/controller/plugins.txt`
- `infra/platform/examples/environment-compose.yaml`
- `infra/platform/examples/second-project.json`
- `infra/platform/library/vars/dotnetPipeline.groovy`
- `infra/platform/projects/identityprovider.json`
- `infra/platform/README.md`
- `infra/platform/scripts/Backup-Platform.ps1`
- `infra/platform/scripts/Connect-GitHub.ps1`
- `infra/platform/scripts/Export-Configuration.ps1`
- `infra/platform/scripts/Get-Plugins.ps1`
- `infra/platform/scripts/Restore-Platform.ps1`
- `infra/platform/scripts/Test-BackupRestore.ps1`
- `infra/platform/scripts/Test-Host.ps1`
- `infra/platform/scripts/Update-PluginLock.ps1`
- `infra/platform/scripts/validate-controller.groovy`
- `infra/platform/scripts/Verify-GitHub.ps1`
- `infra/platform/scripts/Verify-Restore.ps1`
- `scripts/release/project.cjs`

## Infrastructure staging

The deployment copy is prepared by `Export-Configuration.ps1` at `D:/Work/DevOpsLab/portable-platform`. Existing lab files, secrets, volumes, repositories and artifacts are not replaced. Plugin binaries and test evidence are ignored local build/validation outputs, not committed source.
