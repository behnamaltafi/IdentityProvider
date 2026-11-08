using System.Security.Claims;
using IdentityProvider.Infrastructure.Identity;
using IdentityProvider.Infrastructure.Identity.OpenIdConnect;
using IdentityProvider.Infrastructure.Identity.Persistence;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IdentityProvider.Api.Controllers;

public sealed class AuthorizationController(UserManager<IdentityUserAccount> users, IdpPrincipalFactory principals,
    IOpenIddictApplicationManager applications, IOptions<IdentityModuleOptions> settings) : Controller
{
    private const string ServerScheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;

    [HttpGet("~/connect/authorize"), AllowAnonymous, EnableRateLimiting("identity-auth")]
    public async Task<IActionResult> AuthorizeClient()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Missing authorization request.");
        var cookie = await HttpContext.AuthenticateAsync(OpenIdConnectModule.BrowserScheme);
        var ignorePrompt = TempData["Reauthenticated"] as bool? == true;
        var reauthenticate = request.HasPromptValue(PromptValues.Login) || request.MaxAge == 0
            || request.MaxAge.HasValue && cookie.Properties?.IssuedUtc is { } issued
                && DateTimeOffset.UtcNow - issued > TimeSpan.FromSeconds(request.MaxAge.Value);
        if (!cookie.Succeeded || reauthenticate && !ignorePrompt)
        {
            if (request.HasPromptValue(PromptValues.None)) return ProtocolError(Errors.LoginRequired, "Sign-in is required.");
            if (reauthenticate) await HttpContext.SignOutAsync(OpenIdConnectModule.BrowserScheme);
            TempData["Reauthenticated"] = true;
            return Challenge(new AuthenticationProperties { RedirectUri = Request.PathBase + Request.Path + Request.QueryString }, OpenIdConnectModule.BrowserScheme);
        }
        var user = await users.FindByIdAsync(cookie.Principal.FindFirstValue(Claims.Subject));
        if (user is null || !user.Enabled || await users.IsLockedOutAsync(user))
            return ProtocolError(Errors.AccessDenied, "Account is unavailable.");
        var authTime = long.TryParse(cookie.Principal.FindFirstValue(Claims.AuthenticationTime), out var value)
            ? value : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return SignIn(await principals.CreateUserAsync(user, request.GetScopes(), authTime), ServerScheme);
    }

    [HttpPost("~/connect/token"), AllowAnonymous, EnableRateLimiting("identity-auth")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Missing token request.");
        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(ServerScheme);
            if (!result.Succeeded) return ProtocolError(Errors.InvalidGrant, "The grant is no longer valid.");
            var user = await users.FindByIdAsync(result.Principal.FindFirstValue(Claims.Subject));
            if (user is null || !user.Enabled || await users.IsLockedOutAsync(user)
                || result.Principal.FindFirstValue("private_security_stamp") != user.SecurityStamp)
                return ProtocolError(Errors.InvalidGrant, "The session is no longer valid.");
            var grantedScopes = result.Principal.GetScopes();
            if (!string.IsNullOrWhiteSpace(request.Scope))
            {
                var requested = request.GetScopes();
                if (requested.Except(grantedScopes, StringComparer.Ordinal).Any())
                    return ProtocolError(Errors.InvalidScope, "Refresh cannot expand the originally granted scopes.");
                grantedScopes = requested;
            }
            var authTime = long.TryParse(result.Principal.FindFirstValue(Claims.AuthenticationTime), out var value)
                ? value : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var principal = await principals.CreateUserAsync(user, grantedScopes, authTime);
            principal.SetAuthorizationId(result.Principal.GetAuthorizationId());
            return SignIn(principal, ServerScheme);
        }
        if (request.IsClientCredentialsGrantType())
        {
            var application = await applications.FindByClientIdAsync(request.ClientId)
                ?? throw new InvalidOperationException("Client not found after validation.");
            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, "client:" + request.ClientId);
            identity.SetClaim(Claims.ClientId, request.ClientId);
            identity.SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application));
            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetResources(await principals.ResourcesAsync(principal.GetScopes()));
            principal.SetDestinations(_ => [Destinations.AccessToken]);
            return SignIn(principal, ServerScheme);
        }
        return ProtocolError(Errors.UnsupportedGrantType, "This grant type is not supported.");
    }

    [HttpGet("~/connect/userinfo"), HttpPost("~/connect/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UserInfo()
    {
        var user = await users.FindByIdAsync(User.FindFirstValue(Claims.Subject));
        if (user is null || !user.Enabled || await users.IsLockedOutAsync(user)
            || User.FindFirstValue("session_version") != SessionVersion.Create(user.SecurityStamp, settings.Value.OtpHashKey))
            return Unauthorized(new { error = Errors.InvalidToken });
        var result = new Dictionary<string, object> { [Claims.Subject] = user.Id };
        if (User.HasScope(Scopes.Profile)) result[Claims.Name] = user.UserName;
        if (User.HasScope(Scopes.Phone) && user.PhoneNumberConfirmed)
        {
            result[Claims.PhoneNumber] = user.PhoneNumber;
            result[Claims.PhoneNumberVerified] = true;
        }
        if (User.HasScope(Scopes.Email) && !string.IsNullOrWhiteSpace(user.Email))
        {
            result[Claims.Email] = user.Email;
            result[Claims.EmailVerified] = user.EmailConfirmed;
        }
        return Ok(result);
    }

    [HttpGet("~/connect/logout"), AllowAnonymous]
    public IActionResult LogoutConfirmation() => View("Logout");

    [HttpPost("~/connect/logout"), AllowAnonymous, ValidateAntiForgeryToken]
    public IActionResult Logout() => SignOut(new AuthenticationProperties { RedirectUri = "/" },
        OpenIdConnectModule.BrowserScheme, ServerScheme);

    private ForbidResult ProtocolError(string error, string description) => Forbid(new AuthenticationProperties(
        new Dictionary<string, string>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }), ServerScheme);
}
