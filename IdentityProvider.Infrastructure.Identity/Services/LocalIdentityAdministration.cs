using System.Security.Claims;
using IdentityProvider.Application.Identity;
using IdentityProvider.Infrastructure.Identity.Persistence;
using IdentityProvider.Infrastructure.Identity.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IdentityProvider.Infrastructure.Identity.Services;

public sealed class LocalIdentityAdministration(
    UserManager<IdentityUserAccount> users, RoleManager<IdentityRole> roles, IdentityDbContext db, SessionRevoker sessions,
    IOptions<IdpOptions> options) : IIdentityAdministration
{
    public async Task<UserInfo[]> GetUsersAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var page = await users.Users.OrderBy(x => x.Id).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken);
        var result = new List<UserInfo>();
        foreach (var user in page)
            result.Add(new(user.Id, user.UserName, user.PhoneNumber, user.PhoneNumberConfirmed, user.Enabled, (await users.GetRolesAsync(user)).ToArray()));
        return result.ToArray();
    }

    public async Task<RoleInfo[]> GetRolesAsync(CancellationToken cancellationToken)
    {
        var result = new List<RoleInfo>();
        foreach (var role in await roles.Roles.OrderBy(x => x.Name).ToListAsync(cancellationToken))
            result.Add(await DescribeAsync(role));
        return result.ToArray();
    }

    public async Task<IdentityResult<RoleInfo>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 128 || !ValidPermissions(request.Permissions))
            return IdentityResult<RoleInfo>.Fail("InvalidRole", "Supply a role name and permissions from the catalog.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var role = new IdentityRole(request.Name.Trim());
        var created = await roles.CreateAsync(role);
        if (!created.Succeeded) return IdentityResult<RoleInfo>.Fail("InvalidRole", "Role already exists or is invalid.");
        foreach (var permission in request.Permissions.Distinct())
        {
            var result = await roles.AddClaimAsync(role, new Claim(IdentityPermissions.ClaimType, permission));
            if (!result.Succeeded) return IdentityResult<RoleInfo>.Fail("Conflict", "Role update conflicted; retry.");
        }
        await transaction.CommitAsync(cancellationToken);
        return IdentityResult<RoleInfo>.Success(await DescribeAsync(role));
    }

    public async Task<IdentityResult<bool>> SetPermissionsAsync(string roleId, string[] permissions, CancellationToken cancellationToken)
    {
        if (!ValidPermissions(permissions)) return Fail("InvalidPermissions", "Unknown permission.");
        var role = await roles.FindByIdAsync(roleId);
        if (role is null) return Fail("NotFound", "Role not found.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var claim in (await roles.GetClaimsAsync(role)).Where(x => x.Type == IdentityPermissions.ClaimType))
            if (!(await roles.RemoveClaimAsync(role, claim)).Succeeded) return Fail("Conflict", "Role update conflicted; retry.");
        foreach (var permission in permissions.Distinct())
            if (!(await roles.AddClaimAsync(role, new Claim(IdentityPermissions.ClaimType, permission))).Succeeded)
                return Fail("Conflict", "Role update conflicted; retry.");
        await transaction.CommitAsync(cancellationToken);
        foreach (var user in await users.GetUsersInRoleAsync(role.Name))
            await sessions.RevokeAsync(user.Id, cancellationToken);
        return IdentityResult<bool>.Success(true);
    }

    public async Task<IdentityResult<bool>> SetRolesAsync(string userId, string[] roleNames, CancellationToken cancellationToken)
    {
        if (roleNames is null || roleNames.Length > 100 || roleNames.Any(string.IsNullOrWhiteSpace)) return Fail("InvalidRoles", "Invalid roles.");
        var user = await users.FindByIdAsync(userId);
        if (user is null) return Fail("NotFound", "User not found.");
        var requested = roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var name in requested)
            if (!await roles.RoleExistsAsync(name)) return Fail("InvalidRoles", "Role not found.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await users.GetRolesAsync(user);
        var removed = await users.RemoveFromRolesAsync(user, existing.Except(requested, StringComparer.OrdinalIgnoreCase));
        var added = removed.Succeeded
            ? await users.AddToRolesAsync(user, requested.Except(existing, StringComparer.OrdinalIgnoreCase)) : removed;
        if (!added.Succeeded || !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            return Fail("Conflict", "User update conflicted; retry.");
        await transaction.CommitAsync(cancellationToken);
        await sessions.RevokeAsync(user.Id, cancellationToken);
        return IdentityResult<bool>.Success(true);
    }

    public async Task<IdentityResult<bool>> SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return Fail("NotFound", "User not found.");
        user.Enabled = enabled;
        // Rotate the stamp when enabling too: old sessions never become valid again.
        var result = await users.UpdateSecurityStampAsync(user);
        if (result.Succeeded) await sessions.RevokeAsync(user.Id, cancellationToken);
        return result.Succeeded ? IdentityResult<bool>.Success(true) : Fail("Conflict", "User update conflicted; retry.");
    }

    public async Task<IdentityResult<bool>> DeleteRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        var role = await roles.FindByIdAsync(roleId);
        if (role is null) return Fail("NotFound", "Role not found.");
        if ((await users.GetUsersInRoleAsync(role.Name)).Count != 0) return Fail("RoleInUse", "Remove role assignments before deleting it.");
        return (await roles.DeleteAsync(role)).Succeeded ? IdentityResult<bool>.Success(true) : Fail("Conflict", "Role deletion conflicted; retry.");
    }

    private async Task<RoleInfo> DescribeAsync(IdentityRole role) => new(role.Id, role.Name,
        (await roles.GetClaimsAsync(role)).Where(c => c.Type == IdentityPermissions.ClaimType).Select(c => c.Value).Distinct().ToArray());
    private bool ValidPermissions(string[] permissions) => permissions is not null && permissions.Length <= 100
        && permissions.All(p => IdentityPermissions.All.Concat(options.Value.PermissionCatalog).Contains(p, StringComparer.Ordinal));
    private static IdentityResult<bool> Fail(string code, string description) => IdentityResult<bool>.Fail(code, description);
}
