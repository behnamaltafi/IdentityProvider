# فهرست فایل‌های تغییرکرده

مقایسه با پایان مرحله قبلی، commit 762b5e9. فایل‌های برنامه و تست موجود حفظ شده‌اند؛ تغییر CS برنامه‌ای وجود ندارد و سه csproj فقط flag غیرضروری Windows-targeting را از دست داده‌اند.

| وضعیت | فایل |
| --- | --- |
| M | .gitattributes |
| M | .gitignore |
| M | IdentityProvider.Application/IdentityProvider.Application.csproj |
| M | IdentityProvider.Domain/IdentityProvider.Domain.csproj |
| M | IdentityProvider.Infrastructure/IdentityProvider.Infrastructure.csproj |
| A | Dockerfile.release |
| A | Jenkinsfile |
| M | README.md |
| M | Tests/IdentityProvider.Application.UnitTests/Unit/Application/Utilities/Validators/ValidationExtensionsTests.cs |
| A | deploy/environments/internal/.env.example |
| A | deploy/environments/internal/compose.yml |
| A | deploy/environments/production/.env.example |
| A | deploy/environments/production/compose.yml |
| A | deploy/environments/uat/.env.example |
| A | deploy/environments/uat/compose.yml |
| A | docs/CI-CD.fa.md |
| M | docs/versioning.md |
| A | jenkins/Jenkinsfile.promote |
| A | jenkins/Jenkinsfile.release |
| A | jenkins/Jenkinsfile.rollback |
| A | jenkins/flow.groovy |
| A | jenkins/lab/10-jobs.groovy |
| M | package-lock.json |
| M | package.json |
| M | release.config.cjs |
| A | scripts/ci/Convert-TestResults.ps1 |
| M | scripts/ci/Invoke-CI.ps1 |
| M | scripts/ci/Test-Commits.ps1 |
| A | scripts/ci/package-candidate.mjs |
| A | scripts/deploy/Initialize-Environments.ps1 |
| A | scripts/deploy/cli.mjs |
| A | scripts/deploy/deploy.mjs |
| A | scripts/lab/Configure-Jenkins.ps1 |
| A | scripts/lab/Prepare-NuGetCache.ps1 |
| A | scripts/lab/pre-receive |
| A | scripts/release/context.cjs |
| A | scripts/release/lib.mjs |
| A | scripts/release/lifecycle.mjs |
| A | scripts/release/preview-candidate.mjs |
| A | scripts/release/run.mjs |
| A | scripts/tests/Test-FailurePropagation.ps1 |
| A | scripts/tests/pipeline.test.mjs |
| A | scripts/tests/scenarios.mjs |
| M | scripts/tests/test-semantic-release.mjs |
| A | scripts/tests/verify-candidate.mjs |
| A | scripts/tests/verify-deployments.mjs |
| A | scripts/tests/verify-rollback.mjs |
| A | docs/pipeline-verification.fa.md |
| A | docs/pipeline-files.fa.md |

تغییرهای infrastructure خارج مخزن برنامه: docker-compose.yml برای mount فقط‌خواندنی محیط‌ها؛ .gitignore برای runtime محلی و cache؛ jenkins/controller/Dockerfile، plugins.txt، plugins.lock.json و 10-jobs.groovy برای ۷۳ پلاگین pin‌شده و seed افزایشی Jobها؛ environments/internal/compose.yml، environments/uat/compose.yml، environments/production/compose.yml و .env.runtimeهای نادیده‌گرفته‌شده؛ git/IdentityProvider.git/hooks/pre-receive برای منع تغییر تگ؛ artifactهای cache عمومی و گزارش آزمون در artifacts/؛ Jobهای پایدار Jenkins و فهرست تأییدکننده از مدیر واقعی موجود. هیچ origin، رمز، تاریخچه یا تنظیمات global Docker تغییر نکرده است.

runtime secretها، node_modules، bin، obj، dist، artifactها و fixtureها وارد Git نمی‌شوند. در pipeline هیچ GitVersion یا plugin انتشار npm/GitHub/git فعال نیست. Dockerfile چندمرحله‌ای قبلی حفظ شده؛ Dockerfile.release بسته‌بندی جدید فقط publish خروجی مشترک را COPY می‌کند.
