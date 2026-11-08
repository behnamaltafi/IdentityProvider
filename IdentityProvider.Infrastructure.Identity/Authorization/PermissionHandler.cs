using IdentityProvider.Application.Identity;
using IdentityProvider.Infrastructure.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace IdentityProvider.Infrastructure.Identity.Authorization;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

// External providers must emit the same permission claims. No local store is resolved in External mode.
public sealed class ExternalPermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.User.HasClaim(IdentityPermissions.ClaimType, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

// Query current grants so permission removal applies even to already-issued local access tokens.
public sealed class LocalPermissionHandler(UserManager<IdentityUserAccount> users, RoleManager<IdentityRole> roles)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        var id = context.User.FindFirst("sub")?.Value;
        var user = id is null ? null : await users.FindByIdAsync(id);
        if (user is null || !user.Enabled) return;
        foreach (var name in await users.GetRolesAsync(user))
        {
            var role = await roles.FindByNameAsync(name);
            if (role is not null && (await roles.GetClaimsAsync(role)).Any(c => c.Type == IdentityPermissions.ClaimType && c.Value == requirement.Permission))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}
