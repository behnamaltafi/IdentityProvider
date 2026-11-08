using System.Security.Claims;
using System.Collections.Immutable;
using IdentityProvider.Application.Identity;
using IdentityProvider.Infrastructure.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IdentityProvider.Infrastructure.Identity.OpenIdConnect;

public sealed class IdpPrincipalFactory(UserManager<IdentityUserAccount> users, RoleManager<IdentityRole> roles,
    IOpenIddictScopeManager scopes, IOptions<IdentityModuleOptions> settings)
{
    public async Task<ClaimsPrincipal> CreateUserAsync(IdentityUserAccount user, IEnumerable<string> grantedScopes, long authTime)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id);
        identity.SetClaim(Claims.Name, user.UserName);
        identity.SetClaim(Claims.PreferredUsername, user.UserName);
        identity.SetClaim(Claims.AuthenticationTime, authTime);
        identity.SetClaim("private_security_stamp", user.SecurityStamp);
        identity.SetClaim("session_version", SessionVersion.Create(user.SecurityStamp, settings.Value.OtpHashKey));
        if (user.PhoneNumberConfirmed)
        {
            identity.SetClaim(Claims.PhoneNumber, user.PhoneNumber);
            identity.SetClaim(Claims.PhoneNumberVerified, true);
        }
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            identity.SetClaim(Claims.Email, user.Email);
            identity.SetClaim(Claims.EmailVerified, user.EmailConfirmed);
        }
        foreach (var name in await users.GetRolesAsync(user))
        {
            identity.AddClaim(new(Claims.Role, name));
            var role = await roles.FindByNameAsync(name);
            if (role is not null)
                foreach (var claim in await roles.GetClaimsAsync(role))
                    if (claim.Type == IdentityPermissions.ClaimType && !identity.HasClaim(claim.Type, claim.Value))
                        identity.AddClaim(new(claim.Type, claim.Value));
        }
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(grantedScopes);
        principal.SetResources(await ResourcesAsync(principal.GetScopes()));
        principal.SetDestinations(claim => claim.Type switch
        {
            "private_security_stamp" => [],
            Claims.Name or Claims.PreferredUsername when principal.HasScope(Scopes.Profile) => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.PhoneNumber or Claims.PhoneNumberVerified when principal.HasScope(Scopes.Phone) => [Destinations.IdentityToken],
            Claims.Email or Claims.EmailVerified when principal.HasScope(Scopes.Email) => [Destinations.IdentityToken],
            Claims.Subject or Claims.AuthenticationTime => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Role or IdentityPermissions.ClaimType or "session_version" => [Destinations.AccessToken],
            _ => []
        });
        return principal;
    }

    public async Task<string[]> ResourcesAsync(IEnumerable<string> grantedScopes)
    {
        var resources = new List<string>();
        await foreach (var resource in scopes.ListResourcesAsync(grantedScopes.ToImmutableArray())) resources.Add(resource);
        return resources.Distinct(StringComparer.Ordinal).ToArray();
    }
}
