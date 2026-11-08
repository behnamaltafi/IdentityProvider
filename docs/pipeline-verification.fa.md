# گزارش واقعی پیاده‌سازی و دروازه انتشار اول — ۲۰۲۶/۱۱/۰۶

پیاده‌سازی و CI شاخه‌ها آماده بازبینی است، اما کار هنوز از نظر معیار end-to-end کامل نیست: انتشار رسمی اول، استقرار سه محیط، تأیید زنده Production و rollback واقعی منتظر اجازه صریح کاربر هستند. هیچ نتیجه اجرا‌نشده‌ای pass محسوب نشده است.

## شواهد اجراشده

- کنترلر و Registry سالم‌اند؛ linux-agent به Jenkins با WebSocket وصل است و buildهای واقعی روی آن اجرا شده‌اند. ابزارهای Java 21، .NET SDK 10.0.403، Node 24.22.2، npm 11.19.1، Git 2.43.0، Docker 29.9.2، Compose 5.6.1 و PowerShell 7.6.9 با اجرای واقعی بررسی شدند.
- npm ci موفق است. ۱۶ مورد قاعده نسخه و ۹ آزمون سیاست/یکپارچگی موفق‌اند. restore و build Release روی Windows و عامل Linux موفق‌اند. تست‌های واقعی: ۱۵ Unit و ۷ Integration، بدون fail؛ اجرای مجدد اولویت نیز ۱۵ pass و پوشش validator برابر ۹۰٪ است. JUnit و Cobertura واقعی در Jenkins نمایش و archive شدند.
- در Jenkins، feature/cicd-pipeline buildهای ۲ تا ۵ و develop build ۱ موفق شدند. release/cicd-lab buildهای ۱، ۲ و ۴ موفق‌اند؛ build ۴ metadata زیر را بایگانی کرده است. build اول feature به‌علت syntax Groovy شکست خورد، اصلاح شد و اجراهای بعدی سبز شدند. تمام فایل‌های Groovy با runtime واقعی Jenkins compile شدند؛ حذف polling تکراری و انتخاب نسخه rollback نیز اصلاح و بررسی شده‌اند.
- تلاش واقعی commit با پیام نامعتبر توسط hook رد شد و HEAD تغییر نکرد. clone مستقل با commit نامعتبرِ بدون hook توسط همان validator واقعی CI رد شد. تست مستقل با Unit Test عمداً ناموفق باعث خروج ۱ CI و JUnit دارای failure شد؛ تست‌های اصلی حذف یا ضعیف نشدند.
- آزمون مستقل semantic-release روی مخزن موقت، dry-run پیش از انتشار آزمایشی، اولین نسخه fixture برابر 1.0.0، patch برابر 1.0.1، CHANGELOG تجمعی از فایل بازیابی‌شده، docs-only بدون نسخه و feature/release بدون انتشار را تأیید کرد. تگ‌های این fixture فقط داخل مخزن موقت مستقل‌اند و به origin رسمی آزمایشگاه وارد نشده‌اند. شواهد: artifacts/scenario-results.json، artifacts/audit/pipeline-scenarios.log و artifacts/audit/fixture-cumulative-CHANGELOG.md.
- dry-run واقعی feature و release branch هیچ انتشار رسمی ایجاد نمی‌کند. mirror مستقلِ commitهای واقعی candidate، نسخه پیشنهادی 1.0.0 و notes واقعی را از semantic-release می‌گیرد. artifacts/candidate-release-plan.json و candidate-release-notes.md خروجی پیش‌نمایش‌اند؛ manifest رسمی نیستند.
- سوکت فقط روی عامل است؛ کنترلر UID 1000 و صفر executor دارد. گروه سوکت ۰ و UID عامل 1001 است. clone/fetch واقعی bare محلی و دسترسی Registry از عامل موفق‌اند. hook سرور، update/delete تگ پایدار و تگِ هدف‌گرفته روی commit غیر master را با تست stdin رد کرد؛ این تست هیچ ref یا تگی نساخت.
- تمام Composeهای محیط با config --quiet معتبرند. هیچ محیط Internal/UAT/Production هنوز راه‌اندازی نشده است. source و runtime configuration هر محیط مستقل است و secret بیرون Git می‌ماند.

## Artifact واقعی candidate از Jenkins release branch، build ۴

این artifact رسمی نیست و tag پایدار Git ندارد:

```json
{
  "branch": "release/cicd-lab",
  "sourceCommit": "816c1398ee264cd8af10f7611a1cdca7aca32c00",
  "version": "0.0.0-ci.816c1398ee26",
  "official": false,
  "zipSha256": "3e1792e59717dbf5030805f646d6489793825054de8e846a818fa872fe12d626",
  "imageRepository": "localhost:5500/identityprovider/candidates",
  "imageDigest": "sha256:d61ca357125399a8ea5a8b3cfbb5dfe36c4084338745a3dca327b35af9224ced"
}
```

ZIP واقعی از archive Jenkins صادر شد، checksum آن بررسی شد و فایل‌های unzip‌شده با تمام فایل‌های /app همان image به‌صورت byte-for-byte یکسان بودند. کانتینر مقایسه فقط create شد و هرگز اجرا نشد؛ سپس همان کانتینر موقت حذف شد. گزارش: artifacts/audit/release-candidate-byte-verification.json و D:\Work\DevOpsLab\artifacts\audit\release-candidate-build4\.

Jenkins: http://localhost:8080/job/IdentityProvider-Branches/ ؛ Jobهای دیگر: /job/IdentityProvider-Stable/ ، /job/IdentityProvider-Promote/ ، /job/IdentityProvider-Rollback/ . فهرست تأییدکنندگان از حساب مدیریتی موجود admin گرفته شده است، نه هویت ساختگی. مقدار صریح قبلی کاربران overwrite نمی‌شود. حساب admin و رمز آن تغییر نکرده‌اند.

## وضعیت Git و فایل‌ها

feature با Git Flow به develop ادغام و release/cicd-lab از develop ساخته شد. master همچنان روی 762b5e93612d0c5147a638f1b0aae913a63e7589 است. هیچ تگ رسمی وجود ندارد. آخرین SHA شاخه release را با git rev-parse HEAD ببینید؛ commit گزارش فقط مستندات را تغییر می‌دهد. فهرست همه فایل‌های تغییرکرده در pipeline-files.fa.md و راهنمای کامل در CI-CD.fa.md است. NuGet dependencyها به‌روزرسانی نشده‌اند؛ فقط فلگ Windows-targeting غیرلازم از libraryهای خنثی حذف شده است. feed محلی اختیاری از ۱۱۷ package موجود با کنترل SHA512 آماده شد تا EOF دانلودهای اولیه تکرار نشود.

RAM snapshot: کنترلر 670.9 MiB از 2 GiB، عامل 456.5 MiB از 4 GiB و Registry 30.12 MiB از 512 MiB؛ جمع کانتینرها حدود 1.13 GiB است. این عدد مصرف کل VM، daemon و filesystem cache نیست. تنظیمات global Docker Desktop تغییر نکرده‌اند.

## موارد باقی‌مانده و تأیید لازم

دستور بعدی فقط پس از اجازه صریح کاربر اجرا می‌شود:

```powershell
git switch master
git merge --no-ff release/cicd-lab -m "chore(merge): release reviewed CI/CD pipeline"
git push origin master
```

سپس Job IdentityProvider-Stable با PUBLISH=true و در صورت درخواست PROMOTE=true اجرا می‌شود. dry-run master واقعی باید دوباره بررسی شود؛ مقدار 1.0.0 فعلاً فقط پیشنهاد mirror است. input واقعی انتشار توسط کاربر مجاز تأیید می‌شود. ZIP/manifest/CHANGELOG رسمی، تگ و digest فقط در همان اجرای مجاز به وجود می‌آیند. Production پس از Internal و UAT سالم، input مستقل دارد؛ رد کردن آن Production موجود را تغییر نمی‌دهد.

هنوز تأیید نشده‌اند: lifecycle کامل انتشار رسمی و recovery بعد از شکست واقعی، tag رسمی واقعی، manifest رسمی و checksum inventory آن، سلامت Internal/UAT/Production، اجرای زنده approval Production، patch رسمی برنامه و rollback واقعی. برای تست نهایی پس از دو انتشار مجاز:

```bash
node scripts/deploy/cli.mjs list
node scripts/tests/verify-deployments.mjs <actual-approved-version>
# Job Rollback: نسخه قبلی سالم را انتخاب و input واقعی Production را تأیید کنید.
node scripts/tests/verify-rollback.mjs production <actual-previous-version>
```

این فرمان‌ها روی عامل و با ARTIFACT_ROOT=/release-artifacts/identityprovider، ENVIRONMENT_ROOT=/lab/environments و REGISTRY_API_URL=http://registry:5000 اجرا می‌شوند. VERSION باید از manifestهای واقعی انتخاب شود؛ هیچ نسخه، approval یا سلامت فرضی قابل جایگزینی نیست.

ریسک‌های مستند: تراکنش Git و Registry اتمیک نیست؛ recovery و retry باید مطابق راهنما و بدون overwrite انجام شوند. Registry محلی HTTP/RBAC/WORM کامل ندارد و دسترسی socket عامل بسیار قدرتمند است؛ این آزمایشگاه برای کاربران و کد مورد اعتماد است. برای production واقعی، TLS، RBAC، storage immutable، agent مجزا، secret manager و برنامه دیتابیس لازم است.
