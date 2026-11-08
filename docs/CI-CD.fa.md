# راهنمای کامل خط لوله محلی بناک‌مارکت

> این سند معماری قبلی آزمایشگاه محلی را ثبت می‌کند. معماری جدید GitHub و پلتفرم قابل‌حمل در [راهنمای مهاجرت](platform-migration.md) است. آزمایشگاه قبلی تا تأیید و راستی‌آزمایی مهاجرت حفظ می‌شود.

## وضعیت و مرز استفاده

این آزمایشگاه یک شبیه‌سازی محلی روی Windows 11 و Docker Desktop Linux است. نشانی Jenkins برابر http://localhost:8080 و Registry برابر localhost:5500 است. پورت 5000 در ویندوز رزرو بود و تغییر به 5500 با تأیید کاربر انجام شد. فایل‌های زیر پیاده‌سازی اجرایی هستند، ولی انتشار رسمی اول و استقرار Production باید پس از تأیید واقعی انسان اجرا شوند؛ نتیجه آزمون‌های آزمایشی به معنی انتشار رسمی نیست.

## معماری

کنترلر Jenkins با Java 21، کاربر غیر root و صفر executor اجرا می‌شود. عامل linux-agent یک executor دارد و ابزارهای .NET 10، Node 24، npm، Git، PowerShell، Docker CLI و Compose روی آن موجودند. فقط عامل به سوکت Docker دسترسی دارد. این دسترسی عملاً اختیار قدرتمند روی میزبان می‌دهد و فقط برای کد و کاربران مورد اعتماد آزمایشگاه مناسب است.

کد در D:\Work\IdentityProvider قرار دارد. مخزن bare در D:\Work\DevOpsLab\git\IdentityProvider.git حفظ می‌شود؛ در کانتینرها فقط همان مخزن در /git/IdentityProvider.git دیده می‌شود. کنترلر mount فقط خواندنی دارد؛ عامل برای تگ‌های آینده دسترسی نوشتن دارد. Git توسعه‌دهنده همچنان از مسیر ویندوز استفاده می‌کند. Jenkins از file:///git/IdentityProvider.git استفاده می‌کند. استثنای ALLOW_LOCAL_CHECKOUT فقط برای این آزمایشگاه مورد اعتماد فعال است؛ برای نصب مشترک یا غیرقابل اعتماد، سرور محلی Git لازم است.

خروجی‌های رسمی در /release-artifacts/identityprovider نگهداری می‌شوند که به D:\Work\DevOpsLab\artifacts\identityprovider متصل است. فایل‌های محیطی در /lab/environments فقط خواندنی‌اند. مسیر workspace داخل عامل مسیر واقعی میزبان Docker نیست؛ از آن به‌عنوان bind source برای کانتینر دیگر استفاده نکنید. docker build محتوای context را به daemon می‌فرستد و این محدودیت را ندارد.

## راه‌اندازی اولیه و Jobها

ابتدا راهنمای D:\Work\DevOpsLab\docs\JENKINS-SETUP.md را اجرا کنید. رمز مدیر و secret عامل نباید وارد Git یا گفتگو شوند. عامل باید نام linux-agent، برچسب‌های linux docker dotnet10، یک executor و remote root برابر /home/jenkins/agent داشته باشد. ارتباط آن WebSocket است و پورت ورودی agent روی میزبان باز نشده است.

اسکریپت scripts/lab/Configure-Jenkins.ps1 با snapshot رسمی قبلی، پلاگین‌های لازم و وابستگی‌ها را pin می‌کند. فایل jenkins/lab/10-jobs.groovy فقط Jobهای غایب را ایجاد می‌کند؛ تنظیمات امنیتی و Jobهای موجود را بازنویسی نمی‌کند. نسخه آن در controller context و init.groovy.d خانه پایدار Jenkins قرار می‌گیرد. پوشه Multibranch هر دو دقیقه اسکن می‌شود؛ polling جداگانه روی هر Job فعال نیست تا build تکراری بی‌دلیل ایجاد نشود. هیچ webhook خارجی فرض نشده است.

| Job | SCM و مسیر Pipeline | شاخه و اجرا | پارامترها و نگهداری |
| --- | --- | --- | --- |
| IdentityProvider-Branches | file:///git/IdentityProvider.git ، Jenkinsfile | master، develop و feature/*، bugfix/*، release/*، hotfix/*؛ اسکن دوره‌ای یا Scan Now | پارامتر انتشار ندارد؛ ۳۰ build و artifact در Jenkins |
| IdentityProvider-Stable | همان SCM، jenkins/Jenkinsfile.release | فقط master؛ اجرای دستی | PUBLISH پیش‌فرض false؛ PROMOTE پیش‌فرض false؛ ۱۰۰ build و artifact |
| IdentityProvider-Promote | همان SCM، jenkins/Jenkinsfile.promote | ابزار از master؛ اجرای دستی | VERSION دقیق، TARGET=internal/uat/production؛ ۱۰۰ build |
| IdentityProvider-Rollback | همان SCM، jenkins/Jenkinsfile.rollback | ابزار از master؛ اجرای دستی | VERSION قبلی سالم، ENVIRONMENT؛ ۱۰۰ build |

پلاگین‌های مورد نیاز: Git، Pipeline و وابستگی‌ها، Credentials Binding، Input Step، JUnit، Coverage، Lockable Resources و Timestamper. نسخه دقیق تمام وابستگی‌ها در plugins.txt و checksumها در plugins.lock.json ثبت‌اند. مخزن فایل محلی و Registry محلی فعلاً credential خارجی ندارند. برای مهاجرت، credential را در Jenkins Credentials بسازید؛ آن را در URL یا shell چاپ نکنید.

seed در صورت خالی بودن فهرست‌ها، فقط شناسه حساب‌های مدیریتی واقعیِ موجود را در BONAK_RELEASE_APPROVERS و BONAK_PRODUCTION_APPROVERS قرار می‌دهد؛ هویت یا رمز جدید نمی‌سازد و فهرست صریح قبلی را تغییر نمی‌دهد. مدیر می‌تواند در Manage Jenkins > System > Global properties > Environment variables این فهرست‌ها را بازبینی یا محدودتر کند. خالی بودن این دو باعث خطا پیش از تأیید می‌شود، نه تأیید عمومی. مدیران Jenkins ذاتاً می‌توانند input را تأیید کنند. در آزمایشگاه فعلی کاربران واردشده اختیار مدیریتی گسترده دارند؛ پیش از استفاده چندکاربره واقعی، Matrix/Role authorization و تفکیک دسترسی Jobها و SCM ضروری است. نام یا هویت تأییدکننده اختراع نمی‌شود.

Jobهای Stable/Promotion/Rollback قبل از ورود فایل‌های Pipeline به master قابل اجرا نیستند. این وضعیت عمدی است؛ فایل روی feature وجود دارد و ابتدا باید بازبینی و از مسیر release ادغام شود. برای تغییر Jobهای قبلی، از UI یا پیکربندی بررسی‌شده استفاده کنید؛ seed آن‌ها را بی‌اجازه عوض نمی‌کند.

## Git Flow برای توسعه‌دهنده

master شاخه پایدار و develop شاخه یکپارچه‌سازی است. feature و bugfix از develop منشعب می‌شوند و به آن برمی‌گردند. release از develop منشعب می‌شود و با --no-ff به master و develop ادغام می‌شود. hotfix از master شروع و با --no-ff به master و develop برمی‌گردد؛ تغییر آن باید با release باز نیز آشتی داده شود. تگ پایدار فقط توسط semantic-release ساخته می‌شود.

```powershell
git switch develop
git switch -c feature/inventory
# توسعه و آزمون
git add <paths>
git commit -m "feat(inventory): add stock lookup"
git push -u origin feature/inventory
# پس از بازبینی
git switch develop
git merge --no-ff feature/inventory -m "chore(merge): integrate inventory"
# اشکال عادی: git switch -c bugfix/stock-total develop
# آماده‌سازی نسخه
git switch -c release/next develop
# تثبیت، CI و بازبینی خروجی candidate
git switch master
git merge --no-ff release/next -m "chore(merge): integrate reviewed release"
git push origin master
git switch develop
git merge --no-ff release/next -m "chore(merge): reconcile release"
git push origin develop
```

تاریخچه را بازنویسی نکنید؛ از force-push و حذف تگ استفاده نکنید. hook روی bare remote تغییر یا حذف تگ vX.Y.Z را رد می‌کند و تگ جدید باید دقیقاً به master جاری اشاره کند. hook از مسیر scripts/lab/pre-receive قابل بازتولید است؛ hook موجود را بدون بازبینی جایگزین نکنید.

## Conventional Commits و محاسبه نسخه

feat یعنی minor؛ fix و perf و revert یعنی patch؛ علامت ! یا footer نوع BREAKING CHANGE یعنی major. docs، test، ci، chore، style، build، refactor و migration بدون breaking change انتشار ایجاد نمی‌کنند. revert در این پروژه به‌صورت patch مستند شده است؛ analyzer ممکن است جفت commit/revert متناظر را خنثی کند، بنابراین نتیجه واقعی فقط خروجی خود semantic-release است. هیچ اسکریپت دیگری نسخه پایدار را پیش‌بینی یا محاسبه نمی‌کند.

نسخه خصوصی package.json فقط ابزار است. npm ci از lockfile استفاده می‌کند. npm run test:release قواعد را آزمایش می‌کند. npm run test:pipeline یکپارچگی manifest، checksum، ترتیب محیط‌ها و رفتار Registry را می‌آزماید. node scripts/tests/scenarios.mjs آزمون‌های واقعی Git/semantic-release را فقط روی bare آزمایشی مستقل اجرا می‌کند. تگ‌های آن fixture رسمی نیستند و هیچ‌گاه در origin آزمایشگاه ایجاد نمی‌شوند.

پیش‌نمایش عادی: node scripts/release/run.mjs preview. برای بازبینی candidate پیش از ادغام master: node scripts/release/preview-candidate.mjs یک mirror مستقل می‌سازد و فقط dry-run انجام می‌دهد؛ master واقعی تغییر نمی‌کند. semantic-release ممکن است حتی در dry-run جمله Published release را چاپ کند؛ معیار واقعی dryRun=true، نبود تگ رسمی و رد شدن hookهای prepare/publish است، نه آن عبارت نمایشی.

شاخه‌های غیر master فقط CI و candidate با نسخه 0.0.0-ci.<SHA> می‌سازند. candidate در مسیر artifact مربوط به همان build و repository جداگانه identityprovider/candidates نگهداری می‌شود و هرگز manifest رسمی یا تگ پایدار Git ندارد.

## انتشار رسمی: یک بار ساخت

Job Stable ابتدا full history و tags را دریافت و commitها را مستقل از hook محلی بررسی می‌کند. branch صحیح از metadata خود Jenkins خوانده می‌شود؛ detached HEAD باعث تشخیص اشتباه نمی‌شود. پیش‌نمایش semantic-release بایگانی می‌شود. PUBLISH=false فقط CI انجام می‌دهد. انتشار واقعی علاوه بر PUBLISH=true، به input واقعی کاربر مجاز و metadata واقعی Jenkins نیاز دارد.

زیر قفل مشترک Jenkins و قفل فایل پایدار، semantic-release nextRelease.version را می‌دهد. prepare ابتدا CHANGELOG قبلی را از release نهایی قبلی با checksum معتبر بازیابی می‌کند؛ سپس پلاگین changelog ورودی جدید را اضافه می‌کند. هیچ CHANGELOG به master commit نمی‌شود. notes از commitهای واقعی و با نسخه واقعی ساخته می‌شود و source SHA در manifest روشن است.

helper تنها یک بار solution را با نسخه دقیق می‌سازد و Unit/Integration Tests را اجرا می‌کند. سپس dotnet publish --no-build --no-restore یک dist/publish مشترک می‌سازد. ZIP و Dockerfile.release هر دو دقیقاً از همین دایرکتوری استفاده می‌کنند. Dockerfile بسته‌بندی فقط runtime .NET 10 و COPY دارد و هیچ restore/build/publish دیگری انجام نمی‌دهد. props شامل Version، PackageVersion، AssemblyVersion=X.Y.Z.0، FileVersion=X.Y.Z.0، InformationalVersion=X.Y.Z+SHA و ContinuousIntegrationBuild است.

JUnit واقعی از TRX واقعی ساخته می‌شود؛ شکست تست fail است. Cobertura واقعی در Jenkins Coverage نمایش داده می‌شود و حداقل پوشش validatorها ۸۰٪ باقی است. خروجی‌ها حتی در شکست مراحل بعدی archive می‌شوند. OpenAPI همچنان آزمون جداگانه مستندشده است؛ smoke هر محیط، health و Swagger واقعی را بررسی می‌کند.

ZIP SHA256 و digest پاسخ واقعی Registry بررسی می‌شوند. manifest شامل version، tag، source commit/tree، شماره و URL build، نام و checksum ZIP، repository/digest و timestamp UTC است. manifest بیرون ZIP است. SHA256SUMS تمام payload شامل manifest، notes، CHANGELOG، ZIP و publish را پوشش می‌دهد؛ خود inventory داخل checksum خودش نیست.

payload ابتدا در دایرکتوری موقت همان filesystem ذخیره و بررسی می‌شود، سپس atomically در releases/X.Y.Z ظاهر می‌شود. فایل‌ها read-only می‌شوند. وجود release با SHA متفاوت خطاست؛ بازنویسی نمی‌شود. پس از prepare فقط خود semantic-release تگ و notes خود را push می‌کند. publish hook وجود تگ remote، source SHA، payload و digest را بررسی و published/X.Y.Z.json را به‌طور انحصاری می‌سازد. فقط release دارای marker نهایی اجازه استقرار دارد.

digest محتوایی OCI تغییرناپذیر است. helper اجازه بازنویسی tag نسخه موجود را نمی‌دهد و تگ Git با hook محافظت می‌شود. Registry ساده محلی policy کامل RBAC/WORM یا immutable-tag سمت سرور ندارد؛ مدیر صاحب Docker socket یا filesystem می‌تواند این کنترل‌ها را دور بزند. این محدودیت برای آزمایشگاه صریح است؛ production واقعی به Registry با policy عدم بازنویسی، TLS، authorization، امضای artifact و storage قفل‌شده نیاز دارد.

## شکست جزئی و retry

Git و Registry یک تراکنش مشترک ندارند. اگر artifact کامل در storage باشد ولی تگ push نشده باشد، retry با همان source SHA payload قبلی را checksum-check و بدون build/publish مجدد بازیابی می‌کند و semantic-release تگ را می‌سازد. اگر تگ push شده ولی marker نهایی ساخته نشده باشد، checkout دقیق master/tag و اجرای node scripts/release/lifecycle.mjs recover X.Y.Z با مجوز و metadata بررسی‌شده، فقط finalization را انجام می‌دهد؛ نسخه جدید یا تگ رقیب نمی‌سازد.

اگر image push شده ولی ذخیره کامل payload شکست خورده باشد، helper عمداً متوقف می‌شود و tag موجود را overwrite نمی‌کند. workspace و artifacts آن build را حفظ کنید، digest و source و ZIP را با شواهد همان build آشتی دهید و payload ناقص را قرنطینه کنید؛ از rebuild پنهان با همان نسخه خودداری کنید. بازیابی این حالت نیازمند بررسی اپراتور است. قفل فایل پس از crash ممکن است باقی بماند؛ فقط بعد از اطمینان از نبود build فعال و بررسی owner.json آن را بردارید. در shutdown عادی هیچ volume یا artifact حذف نمی‌شود.

به‌منظور CHANGELOG داخل Git در آینده، ساده‌ترین روش یک شاخه مستندات مستقل است. اگر قرار است روی master tracked شود، باید یک commit بازبینی‌شده قبل از build ایجاد شود تا source SHA/tree و تگ همچنان دقیقاً به منبع ساخت اشاره کنند. افزودن git plugin یا commit پس از build بدون طراحی مجدد، traceability را می‌شکند؛ در پیاده‌سازی فعلی مجاز نیست.

## Internal، UAT و Production

سه پروژه Compose مستقل bonak-internal، bonak-uat و bonak-production با networkهای مستقل و پورت‌های loopback 8081، 8082 و 8083 وجود دارند. runtime configuration هر محیط در D:\Work\DevOpsLab\environments/<env>/.env.runtime خارج Git است و مقدار واقعی secret نباید در example قرار گیرد. دیتابیس لازم برای health یا تست‌های فعلی نیست و SQL Server اضافه نشده است.

scripts/deploy/Initialize-Environments.ps1 فایل‌های موجود را بی‌اجازه overwrite نمی‌کند. استقرار فقط imageRepository@imageDigest معتبر از manifest نهایی را pull می‌کند؛ build ندارد. Compose --wait دارای مهلت ۱۲۰ ثانیه است، healthcheck bounded است و smoke روی network همان محیط اجرا می‌شود. failure باعث توقف progression و ثبت رکورد failed می‌شود؛ artifact رسمی حذف نمی‌شود.

Promotion ابتدا Internal و smoke، سپس UAT و smoke را اجرا می‌کند. Production فقط بعد از رکورد UAT سالم با همان digest و input واقعی Jenkins انجام می‌شود. submitter محدود است و شناسه واقعی approver ذخیره می‌شود. approval به build، نسخه، digest، نوع عملیات، رکورد UAT و hash پیکربندی متصل است، انقضا دارد و یک بار مصرف است. رد کردن یا abort input هیچ فرمانی علیه Production اجرا نمی‌کند و Production قبلی حفظ می‌شود. تاریخچه deployment و approval در storage پایدار JSONهای مستقل دارد.

دستور verify-deployments.mjs بعد از انتشار و تأیید واقعی، هر سه کانتینر را inspect می‌کند و یکسانی digest و سلامت و وجود approval را می‌سنجد؛ بدون شواهد واقعی pass گزارش نمی‌کند.

## Rollback

Job Rollback یا node scripts/deploy/cli.mjs list manifestهای نهایی و سالم را فهرست می‌کند. اپراتور نسخه قبلی را انتخاب می‌کند. rollback فقط نسخه‌ای را می‌پذیرد که قبلاً در همان محیط سالم بوده، checksum و digest آن معتبر باشد، image هنوز در Registry موجود باشد و hash پیکربندی محیط با deployment قبلی برابر باشد. در صورت تغییر configuration، بررسی سازگاری صریح لازم است و عملیات fail-closed می‌شود.

Rollback Production نیز input واقعی دارد و actor و build و digest را ثبت می‌کند. هیچ build یا republish و هیچ حذف release جدیدی انجام نمی‌شود. بعد از استقرار سلامت و smoke دوباره اجرا می‌شود. در آینده migrationهای دیتابیس به برنامه سازگاری و rollback مستقل نیاز دارند؛ برگشت image تضمین برگشت schema نیست.

## عیب‌یابی و مهاجرت

برای failure ابتدا console همان Jenkins build، JUnit، Coverage و artifacts archive را ببینید. خطای compile Groovy قبل از گرفتن agent رخ می‌دهد؛ شکست NuGet/network را با حذف تست حل نکنید. خطای Registry hostname معمولاً ناشی از اشتباه گرفتن localhost کانتینر با daemon است. در agent shell از registry:5000 و در Docker tag از localhost:5500 استفاده کنید. سوکت باید از group_add عددی درست خواندنی/نوشتنی باشد؛ controller را root نکنید و privileged فعال نکنید.

در مهاجرت به Bitbucket Server/Data Center، remote را با هماهنگی تغییر دهید، credentials محدود Jenkins بسازید، SCM URL و branch source را عوض کنید، full history/tags و محافظت master و تگ‌ها را حفظ کنید و سیاست polling/webhook را بازبینی کنید. پس از حذف file remote، استثنای local checkout و mount bare را حذف کنید. قبل از انتقال، SHA و tagها را دو طرف تطبیق دهید؛ تاریخچه را بازنویسی نکنید.

برای Artifactory، abstraction ذخیره immutable release و بازیابی previous CHANGELOG را با checksum و create-if-absent پیاده کنید؛ ساختار manifest و digest تغییر نکند. حساب Jenkins حداقل دسترسی و token secret داشته باشد. retention مربوط به نسخه‌های قابل rollback را با promotion status هماهنگ کنید؛ حذف Jenkins build نباید release رسمی را حذف کند.

پیش از production واقعی: TLS و احراز هویت Registry، RBAC واقعی، agentهای مجزا یا rootless build به‌جای socket مشترک، credential rotation، backup و آزمون restore، WORM/immutable artifact storage، امضای image و provenance، اسکن آسیب‌پذیری، سیاست شبکه و secret manager، محدودیت دسترسی release و promotion، مانیتورینگ و برنامه دیتابیس لازم است. سه Compose روی یک Docker Desktop جداسازی واقعی production نیستند.

برای جلوگیری از دانلود تکراری، scripts/lab/Prepare-NuGetCache.ps1 می‌تواند بسته‌های واقعی ارجاع‌شده در project.assets.json را با کنترل SHA512 به feed محلی اختیاری منتقل کند. secret یا تنظیمات NuGet کپی نمی‌شود؛ بسته‌ها و نسخه‌ها تغییر نمی‌کنند و nuget.org برای موارد غایب باقی می‌ماند. فلگ غیرضروری EnableWindowsTargeting از class libraryهای net10.0 حذف شده است؛ آن‌ها API ویندوز، WPF یا WinForms ندارند.
