using System.Security.Claims;
using IdentityProvider.Application.Identity;
using IdentityProvider.Infrastructure.Identity;
using IdentityProvider.Infrastructure.Identity.OpenIdConnect;
using IdentityProvider.Infrastructure.Identity.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IdentityProvider.Api.Pages.Account;

[AllowAnonymous, EnableRateLimiting("identity-auth")]
public sealed class LoginModel(IIdentityService identity, UserManager<IdentityUserAccount> users,
    IOptions<IdentityModuleOptions> settings) : PageModel
{
    [BindProperty(SupportsGet = true)] public string ReturnUrl { get; set; } = "/";
    [BindProperty] public string UserName { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string PhoneNumber { get; set; } = "";
    [BindProperty] public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public void OnGet() => Message = TempData["Message"] as string ?? "";

    public async Task<IActionResult> OnPostPasswordAsync(CancellationToken cancellationToken)
    {
        var result = await identity.AuthenticatePasswordAsync(new(UserName, Password), cancellationToken);
        if (result.Succeeded) return await CompleteAsync(result.Value.Id);
        Message = "نام کاربری یا رمز عبور نامعتبر است یا ورود موقتاً امکان‌پذیر نیست.";
        return Page();
    }

    public async Task<IActionResult> OnPostRegisterAsync(CancellationToken cancellationToken)
    {
        var result = await identity.RegisterAsync(new(UserName, Password), cancellationToken);
        if (result.Succeeded) return await CompleteAsync(result.Value.Id);
        Message = "ثبت‌نام انجام نشد. نام کاربری یکتا و رمز حداقل ۱۲ کاراکتری با حروف بزرگ و کوچک، عدد و علامت وارد کنید.";
        return Page();
    }

    public async Task<IActionResult> OnPostSmsAsync(CancellationToken cancellationToken)
    {
        var result = await identity.AuthenticateSmsAsync(new(PhoneNumber, Code), cancellationToken);
        if (result.Succeeded) return await CompleteAsync(result.Value.Id);
        Message = "کد ورود نامعتبر، منقضی یا قبلاً مصرف شده است.";
        return Page();
    }

    private async Task<IActionResult> CompleteAsync(string id)
    {
        var user = await users.FindByIdAsync(id);
        var claims = new ClaimsIdentity(OpenIdConnectModule.BrowserScheme, "name", "role");
        claims.AddClaim(new("sub", user.Id));
        claims.AddClaim(new("name", user.UserName));
        claims.AddClaim(new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
        claims.AddClaim(new("session_version", SessionVersion.Create(user.SecurityStamp, settings.Value.OtpHashKey)));
        await HttpContext.SignInAsync(OpenIdConnectModule.BrowserScheme, new ClaimsPrincipal(claims),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }
}
