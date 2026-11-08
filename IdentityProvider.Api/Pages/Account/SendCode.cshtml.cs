using IdentityProvider.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace IdentityProvider.Api.Pages.Account;

[AllowAnonymous, EnableRateLimiting("identity-sms")]
public sealed class SendCodeModel(IIdentityService identity) : PageModel
{
    [BindProperty] public string ReturnUrl { get; set; } = "/";
    [BindProperty] public string PhoneNumber { get; set; } = "";
    public IActionResult OnGet() => RedirectToPage("/Account/Login");
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await identity.RequestSmsAsync(new(PhoneNumber), cancellationToken);
        TempData["Message"] = result.Succeeded ? "کد ورود ارسال شد. شماره و کد را در بخش تأیید وارد کنید."
            : result.Errors[0].Code == "TooManyRequests" ? "برای ارسال دوباره کد کمی صبر کنید." : "ارسال پیامک انجام نشد؛ شماره یا دسترسی سرویس پیامک را بررسی کنید.";
        return RedirectToPage("/Account/Login", new { returnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/" });
    }
}
