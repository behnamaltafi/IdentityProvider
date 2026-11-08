# هویت، کاربران، نقش‌ها و مجوزها

> این فایل مرجع نسخه اولیه کتابخانه محلیِ کپی‌شده است. برای راه‌اندازی و رفتار نسخه مستقل OAuth2/OIDC، [راهنمای IdP مستقل](idp.fa.md) مرجع فعلی است؛ تنظیمات و محدودیت‌های نسخه اولیه از جمله نبود refresh token و discovery درباره نسخه مستقل صدق نمی‌کنند.

پیاده‌سازی در `IdentityProvider.Infrastructure.Identity` یک Class Library مستقل در بخش Infrastructure سولوشن است. وابستگی آن فقط به Application است؛ به دیتابیس و repositoryهای کسب‌وکار وابسته نیست. قراردادهای `IIdentityService`، `IIdentityAdministration` و `ISmsSender` و DTOها در `IdentityProvider.Application/Identity` قرار دارند. هیچ نوعی از EF، ASP.NET Identity یا ارائه‌دهنده توکن وارد قراردادهای Application نشده است.

سایت IdentityBase بررسی شد. ایده تفکیک ماژول‌های کاربران، RBAC و احراز هویت از آن گرفته شده؛ در این تغییر پکیج‌های Identity.Base نصب نشده‌اند. پیاده‌سازی فعلی از ASP.NET Core Identity برای کاربر، هش رمز، نقش و قفل موقت استفاده می‌کند. حالت Local توکن JWT با امضای RSA صادر می‌کند و یک سیستم ورود برای همین API است؛ سرویس کامل OAuth2/OIDC، discovery، PKCE، SSO و refresh token نیست. برای این قابلیت‌ها، حالت External را به IdentityBase/OpenIddict یا IdP انتخابی متصل کنید.

## دو حالت اجرا

| تنظیم | رفتار |
| --- | --- |
| `Identity:Provider=Local` | دیتابیس هویت، ورود با رمز/پیامک، مدیریت کاربران و نقش‌ها و endpointهای محلی فعال می‌شوند. کلید و اتصال دیتابیس لازم است. |
| `Identity:Provider=External` | فقط JWT ارائه‌دهنده خارجی اعتبارسنجی می‌شود. دیتابیس و endpointهای هویت محلی ثبت نمی‌شوند. تنظیمات `Authentication:Authority` و `Authentication:Audience` استفاده می‌شوند. |

پیش‌فرض فایل تنظیمات External است تا برنامه بدون دیتابیس و secret قابل اجرا بماند. برای استفاده از ورودهای جدید Local را تنظیم کنید. مقدار Provider اشتباه پذیرفته نمی‌شود و تنظیمات ناقص Local هنگام شروع برنامه خطا می‌دهند.

## راه‌اندازی Local

SQL Server و درگاه پیامک را باید خودتان مشخص کنید. هیچ رمز، کلید خصوصی یا اتصال واقعی در repository قرار نگرفته و هیچ migration روی دیتابیس واقعی اجرا نشده است.

نمونه تنظیمات یک نشست توسعه در PowerShell، از ریشه پروژه:

```powershell
$env:Identity__Provider = 'Local'
$env:Identity__Issuer = 'https://localhost:7001'
$env:Identity__Audience = 'identityprovider-api'
$env:ConnectionStrings__IdentityDbContext = '<SQL Server connection string>'

# برای توسعه؛ کلید تولیدشده در این نشست را برای اجراهای بعدی در secret store نگه دارید.
$identityRsa = [System.Security.Cryptography.RSA]::Create(3072)
try { $env:Identity__SigningKeyPem = $identityRsa.ExportRSAPrivateKeyPem() }
finally { $identityRsa.Dispose() }
$env:Identity__OtpHashKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

$env:Sms__Endpoint = 'https://<your-sms-gateway>/login-code'
$env:Sms__ApiKey = '<gateway API key>'
```

کلید RSA حداقل ۲۰۴۸ بیت و کلید هش OTP حداقل ۳۲ کاراکتر لازم است. برای محیط اصلی این کلیدها را به‌صورت پایدار در secret store مدیریت کنید. تغییر کلید RSA توکن‌های جاری را بی‌اعتبار می‌کند؛ تغییر کلید OTP، کدهای پیامکی جاری را. Issuer آدرس صادرکننده توکن است و باید HTTPS باشد؛ پیاده‌سازی Local روی آن discovery منتشر نمی‌کند.

Migration مستقل هویت در `IdentityProvider.Infrastructure.Identity/Persistence/Migrations` قرار دارد. با EF CLI نسخه ۱۰ اجرا کنید؛ اگر ابزار موجود ندارید آن را با نسخه سازگار .NET 10 نصب کنید. برای مثال ابزار محلی:

```powershell
dotnet tool install dotnet-ef --local --version 10.0.0
dotnet ef migrations script --idempotent --project IdentityProvider.Infrastructure.Identity --context IdentityDbContext --output artifacts/identity.sql
# پس از بررسی SQL و اطمینان از اتصال دیتابیس موردنظر:
dotnet ef database update --project IdentityProvider.Infrastructure.Identity --context IdentityDbContext
```

Design-time factory فقط `ConnectionStrings__IdentityDbContext` را از environment می‌خواند. دیتابیس کسب‌وکار همچنان مستقل است. جداول هویت در schema به نام `identity` ساخته می‌شوند. برنامه هنگام startup migration اجرا نمی‌کند.

برای ساخت اولین مدیر، یک بار و پیش از بازکردن ثبت‌نام عمومی اجرا کنید:

```powershell
$env:Identity__BootstrapUserName = '<initial administrator user name>'
$env:Identity__BootstrapPassword = '<strong unique password>'
dotnet run --project IdentityProvider.Api -- --initialize-identity
Remove-Item Env:Identity__BootstrapUserName
Remove-Item Env:Identity__BootstrapPassword
dotnet run --project IdentityProvider.Api -- --urls https://localhost:7001
```

رمز حداقل ۱۲ کاراکتر، شامل حروف بزرگ/کوچک، عدد و علامت لازم دارد. این فرمان نقش `IdentityAdministrator` و کاربر اولیه را در یک تراکنش می‌سازد و برنامه را می‌بندد. اگر نام کاربری قبلاً وجود داشته باشد، فرمان متوقف می‌شود و آن حساب را ارتقا نمی‌دهد. اجرای عادی هیچ کاربری را مدیر نمی‌کند. ساخت کاربر/نقش و عملیات مدیریت نیز از همین قواعد رمز و بررسی نتیجه Identity استفاده می‌کنند.

## درگاه پیامک

`HttpSmsSender` یک قرارداد gateway ساده دارد:

```http
POST https://<gateway>/login-code
Authorization: Bearer <Sms:ApiKey>
Content-Type: application/json

{"phoneNumber":"+989123456789","code":"123456"}
```

پاسخ موفق 2xx یعنی پذیرش ارسال؛ رسید تحویل پیامک بررسی نمی‌شود. کد واقعی تصادفی است و هرگز در پاسخ API یا log ثبت نمی‌شود. redirect غیرفعال است تا کلید درگاه به مقصد دیگری ارسال نشود. timeout ارسال ۱۵ ثانیه است. خطای ارسال با 503 برمی‌گردد و کد مربوطه باطل می‌شود. retry خودکار ارسال وجود ندارد.

این قرارداد مخصوص هیچ پنل پیامکی مشخصی نیست. اگر پنل شما URL، API key header یا template متفاوت دارد، یک adapter از `ISmsSender` بسازید و آن را در DI ثبت کنید. ثبت پیش‌فرض با TryAdd انجام می‌شود؛ پیاده‌سازی شما را می‌توان پیش از `AddIdentityModule` ثبت کرد، یا پس از آن جایگزین نمود. برای تست از sender حافظه‌ای استفاده شده؛ sender تست در برنامه اصلی ثبت نمی‌شود.

## endpointهای ورود و حساب

همه مسیرهای زیر زیرمجموعه `/api/identity` هستند. توکن در header به شکل `Authorization: Bearer <accessToken>` ارسال می‌شود. ثبت‌نام با رمز، توکن صادر نمی‌کند؛ بعد از ثبت‌نام login را فراخوانی کنید.

| متد و مسیر | ورودی | دسترسی و رفتار |
| --- | --- | --- |
| `POST /register` | `{userName,password}` | عمومی؛ ساخت حساب بدون نقش |
| `POST /login` | `{userName,password}` | عمومی؛ دریافت access token |
| `POST /sms/request` | `{phoneNumber}` | عمومی؛ درخواست کد |
| `POST /sms/verify` | `{phoneNumber,code}` | عمومی؛ ورود یا ساخت حساب پس از تأیید شماره |
| `GET /me` | — | اطلاعات حساب احراز هویت‌شده |
| `POST /phone/request` | `{phoneNumber}` | درخواست کد اتصال شماره به حساب جاری |
| `POST /phone/verify` | `{phoneNumber,code}` | تأیید شماره و اتصال به حساب جاری؛ نشست‌های قبلی باطل می‌شوند |
| `POST /password/set` | `{newPassword}` | فقط حساب جاری که هنوز رمز ندارد؛ مناسب حساب پیامکی |
| `POST /password` | `{currentPassword,newPassword}` | تغییر رمز حساب جاری؛ نشست‌های قبلی باطل می‌شوند |
| `POST /logout-all` | — | لغو تمام نشست‌های حساب جاری |

ورودی `09123456789` به `+989123456789` تبدیل می‌شود؛ شماره‌های E.164 نیز پذیرفته می‌شوند. کد ۶ رقمی است. مقدار code باید string باشد تا صفر ابتدایی حفظ شود. حساب پیامکی نام کاربری `sms_989123456789` دارد که از `/me` برمی‌گردد؛ ثبت‌نام عمومی با پیشوند `sms_` مجاز نیست. پس از تعیین رمز، همین userName را برای login استفاده کنید.

برای ورود با پیامک به حسابی که قبلاً با رمز ساخته شده، ابتدا با رمز وارد شوید و `/phone/request` و `/phone/verify` را اجرا کنید. تأیید شماره، نشست جاری را هم باطل می‌کند؛ سپس با پیامک یا رمز دوباره وارد شوید. کد اتصال شماره فقط برای همان کاربر قابل مصرف است و در `/sms/verify` پذیرفته نمی‌شود. شماره متصل به حساب دیگری منتقل یا دو حساب خودکار ادغام نمی‌شوند.

## مدیریت کاربران، نقش‌ها و دسترسی

مسیرهای زیر زیرمجموعه `/api/identity/admin` هستند:

| متد و مسیر | ورودی | مجوز |
| --- | --- | --- |
| `GET /users?skip=0&take=25` | — | `identity.users.read`؛ حداکثر ۱۰۰ کاربر در صفحه |
| `GET /users/{id}` | — | `identity.users.read` |
| `PUT /users/{id}/roles` | `{roles:["Reader"]}` | `identity.users.write`؛ جایگزینی کل نقش‌ها |
| `PUT /users/{id}/enabled` | `{enabled:false}` | `identity.users.write` |
| `POST /users/{id}/revoke-sessions` | — | `identity.users.write` |
| `GET /roles` | — | `identity.roles.read` |
| `POST /roles` | `{name,permissions:[...]}` | `identity.roles.write` |
| `PUT /roles/{id}/permissions` | `{permissions:[...]}` | `identity.roles.write`؛ جایگزینی کل مجوزها |
| `DELETE /roles/{id}` | — | `identity.roles.write`؛ فقط نقش بدون عضو |
| `GET /permissions` | — | `identity.roles.read`؛ کاتالوگ مجوزها |

ایجاد حساب از مسیر ثبت‌نام و مشاهده/تغییر نقش‌ها و فعال‌بودن از مدیریت انجام می‌شود. حذف کاربر پیاده نشده است؛ غیرفعال‌سازی از باقی‌ماندن ارجاعات کاربر جلوگیری می‌کند. ویرایش دلخواه profile، بازیابی رمز، MFA، سازمان‌ها، refresh token و پنل گرافیکی مدیریت جزو این تغییر نیستند.

مجوزها در کاتالوگ `IdentityPermissions.All` تعریف می‌شوند و انتساب آن‌ها به نقش در دیتابیس است. برای مجوز جدید کسب‌وکار ثابت مربوطه را به کاتالوگ اضافه کنید و روی endpoint از policy همان نام استفاده کنید:

```csharp
[Authorize(Policy = IdentityPermissions.UsersRead)]
// یا در Minimal API:
// .RequireAuthorization(IdentityPermissions.UsersRead)
```

برای محدودیت خودکار اضافه‌ای بر نقش مدیر تکیه نشده است؛ مدیر دارای مجوز نوشتن کاربران می‌تواند نقش حساب خودش را هم تغییر دهد. برای حفظ دسترسی عملیاتی، بیش از یک مدیر قابل اتکا نگه دارید.

## کنترل‌های اجراشده

- کد پیامک با HMAC-SHA256، کلید محرمانه و salt مستقل در دیتابیس ذخیره می‌شود. مقدار خام ذخیره نمی‌شود. مقایسه هش زمان ثابت دارد.
- انقضای پیش‌فرض کد ۱۲۰ ثانیه، فاصله ارسال ۶۰ ثانیه و سقف حدس ۵ تلاش است. Version به‌عنوان concurrency token، مصرف و تعداد تلاش را میان instanceها محافظت می‌کند. صدور کد جدید قبلی را جایگزین می‌کند.
- درخواست پیامک به تفکیک IP حداکثر ۵ بار در ۱۰ دقیقه و endpointهای ورود/حساب ۲۰ بار در دقیقه پذیرفته می‌شوند. شمارنده IP حافظه‌ای و متعلق به هر instance است؛ cooldown شماره و مصرف کد در دیتابیس مشترک‌اند. برای استقرار چند replica یا مقابله با ارسال انبوه روی شماره‌های مختلف، محدودیت مشترک در ingress/gateway تنظیم کنید. اگر reverse proxy دارید، forwarded headers را فقط برای proxyهای مورد اعتماد تنظیم کنید؛ برنامه عمداً header دلخواه X-Forwarded-For را مستقیماً قبول نمی‌کند.
- بعد از ۵ رمز اشتباه حساب ۱۵ دقیقه قفل می‌شود. توکن محلی کاربر قفل‌شده یا غیرفعال‌شده پذیرفته نمی‌شود.
- JWT محلی با RS256 امضا می‌شود. issuer، audience، زمان انقضا، امضا و الگوریتم بررسی می‌شوند. اعتبار پیش‌فرض ۱۵ دقیقه و clock skew برابر ۱۵ ثانیه است. refresh token صادر نمی‌شود؛ پس از انقضا باید دوباره وارد شد.
- در حالت Local، security stamp و وضعیت فعلی کاربر در هر درخواست بررسی می‌شوند. لغو نشست، تغییر رمز/شماره، تغییر نقش‌های کاربر و غیرفعال‌سازی نشست‌ها را بی‌اعتبار می‌کنند. فعال‌کردن مجدد حساب، توکن قدیمی را معتبر نمی‌کند.
- policyهای مجوز محلی، نقش‌ها و مجوزهای فعلی دیتابیس را بررسی می‌کنند؛ حذف یک مجوز حتی روی access token قبلی مؤثر است. role و permission در JWT نیز وجود دارند، اما API محلی برای این policyها به snapshot توکن اکتفا نمی‌کند.
- ویرایش چندمرحله‌ای نقش و انتساب نقش‌ها در تراکنش انجام می‌شود. حذف نقش دارای عضو رد می‌شود. هیچ wildcard یا ارتقای خودکار کاربر جدید وجود ندارد.

## انتقال به سرویس هویت مستقل یا IdP

دو مسیر وجود دارد:

1. همین کتابخانه را در یک ASP.NET host مستقل reference کنید، `AddIdentityModule` و `MapIdentityModule` و middlewareهای احراز هویت، authorization و rate limiter را آنجا اجرا کنید. اتصال مستقل هویت و شناسه کاربران حفظ می‌شوند و کلید خصوصی فقط در host هویت می‌ماند. برای تبدیل این host به IdP استاندارد، OpenIddict/IdentityBase و discovery/JWKS و جریان استاندارد ورود را در آن سرویس اضافه کنید؛ کتابخانه Local فعلی این بخش را ارائه نمی‌کند.
2. از IdP آماده مانند IdentityBase استفاده کنید. در API کسب‌وکار Provider را External کنید و `Authentication:Authority` و `Authentication:Audience` را تنظیم کنید. API سپس metadata و کلیدهای عمومی را از Authority دریافت می‌کند و به دیتابیس هویت محلی وصل نمی‌شود.

در حالت External، issuer باید `sub` به‌عنوان شناسه ثابت کاربر، `role` برای نقش‌ها و یک یا چند claim به نام `permission` برای مجوزها صادر کند. `MapInboundClaims=false` است. policyهای موجود بر اساس همین مجوزها کار می‌کنند. اگر IdP claim دیگری صادر می‌کند، در adapter/claims transformation آن را نگاشت کنید. `sub` کاربران موجود را هنگام مهاجرت حفظ کنید یا نگاشت هویت صریح داشته باشید تا ارجاعات کسب‌وکار قطع نشوند.

با تعویض به External، کلاینت ورود/ثبت‌نام را از IdP انجام می‌دهد و endpointهای محلی از API کسب‌وکار حذف می‌شوند. مدیریت کاربر و پیامک نیز به سرویس هویت منتقل می‌شوند. APIهای مدیریت IdPهای مختلف یکسان نیستند؛ اگر Application در آینده لازم دارد از داخل یک use case کاربر مدیریت کند، adapter آن ارائه‌دهنده را برای `IIdentityAdministration`/`IIdentityService` ثبت کنید. adapter راه دور vendor-specific در این تغییر نوشته نشده است. قواعد دامنه و authorization کسب‌وکار به EF Identity وابسته نمی‌شوند.

در External، ابطال آنی توکن و تازه‌شدن مجوزها مسئولیت IdP و طول عمر توکن/سازوکار ابطال انتخابی آن است؛ بررسی stamp محلی انجام نمی‌شود.

## اعتبارسنجی

```powershell
dotnet build IdentityProvider.slnx
dotnet test IdentityProvider.slnx --no-build
dotnet test Tests/IdentityProvider.Infrastructure.IntegrationTests --filter FullyQualifiedName~Identity
```

تست‌های هویت با SQLite واقعی حافظه‌ای و gateway پیامک تست اجرا می‌شوند. ثبت‌نام و ورود، قفل حساب، کنترل مجوزها، تغییر نقش و حذف مجوز از نشست موجود، لغو نشست، غیرفعال‌سازی، انقضا/حدس/مصرف مجدد/رقابت OTP، اتصال شماره، تعیین و تغییر رمز، محدودیت ارسال، خطای درگاه، توکن با امضای دستکاری‌شده یا issuer/audience/expiry نامعتبر و حالت External پوشش داده شده‌اند. تست‌ها پیامک واقعی ارسال نمی‌کنند. اجرای migration روی SQL Server و اتصال پنل پیامک واقعی نیاز به تنظیمات محیط مقصد دارد.
