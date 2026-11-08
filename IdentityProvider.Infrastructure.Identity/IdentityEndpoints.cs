using System.Security.Claims;
using IdentityProvider.Application.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IdentityProvider.Infrastructure.Identity.OpenIdConnect;

namespace IdentityProvider.Infrastructure.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetRequiredService<IOptions<IdentityModuleOptions>>().Value.Provider != "Local") return endpoints;
        var auth = endpoints.MapGroup("/api/identity").WithTags("Identity").RequireRateLimiting("identity-auth");
        auth.MapPost("/register", async (RegisterRequest request, IIdentityService service, CancellationToken ct) =>
            Respond(await service.RegisterAsync(request, ct))).AllowAnonymous();
        var settings = endpoints.ServiceProvider.GetRequiredService<IOptions<IdentityModuleOptions>>().Value;
        if (settings.EnableDirectLogin)
            auth.MapPost("/login", async (PasswordLoginRequest request, IIdentityService service, CancellationToken ct) =>
                Respond(await service.LoginAsync(request, ct))).AllowAnonymous();
        // No account-existence response; successful verification signs in or creates the account.
        endpoints.MapPost("/api/identity/sms/request", async (SmsRequest request, IIdentityService service, CancellationToken ct) =>
            Respond(await service.RequestSmsAsync(request, ct)))
            .WithTags("Identity").RequireRateLimiting("identity-sms").AllowAnonymous();
        if (settings.EnableDirectLogin)
            auth.MapPost("/sms/verify", async (SmsLoginRequest request, IIdentityService service, CancellationToken ct) =>
                Respond(await service.LoginSmsAsync(request, ct))).AllowAnonymous();
        auth.MapGet("/me", async (ClaimsPrincipal principal, IIdentityService service, CancellationToken ct) =>
        {
            var user = await service.GetUserAsync(principal.FindFirstValue("sub"), ct);
            return user is null ? Results.NotFound() : Results.Ok(user);
        }).RequireAuthorization();
        auth.MapPost("/password", async (ChangePasswordRequest request, ClaimsPrincipal principal, IIdentityService service, CancellationToken ct) =>
            Respond(await service.ChangePasswordAsync(principal.FindFirstValue("sub"), request, ct))).RequireAuthorization();
        auth.MapPost("/password/set", async (SetPasswordRequest request, ClaimsPrincipal principal, IIdentityService service, CancellationToken ct) =>
            Respond(await service.SetPasswordAsync(principal.FindFirstValue("sub"), request, ct))).RequireAuthorization();
        endpoints.MapPost("/api/identity/phone/request", async (SmsRequest request, ClaimsPrincipal principal, IIdentityService service, CancellationToken ct) =>
            Respond(await service.RequestPhoneVerificationAsync(principal.FindFirstValue("sub"), request, ct)))
            .WithTags("Identity").RequireRateLimiting("identity-sms").RequireAuthorization();
        auth.MapPost("/phone/verify", async (SmsLoginRequest request, ClaimsPrincipal principal, IIdentityService service, CancellationToken ct) =>
            Respond(await service.VerifyPhoneAsync(principal.FindFirstValue("sub"), request, ct))).RequireAuthorization();
        auth.MapPost("/logout-all", async (ClaimsPrincipal principal, IIdentityService service, CancellationToken ct) =>
        {
            await service.RevokeSessionsAsync(principal.FindFirstValue("sub"), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        var admin = endpoints.MapGroup("/api/identity/admin").WithTags("Identity administration").RequireAuthorization();
        admin.MapGet("/users", async (IIdentityAdministration service, CancellationToken ct, int skip = 0, int take = 25) =>
            Results.Ok(await service.GetUsersAsync(skip, take, ct))).RequireAuthorization(IdentityPermissions.UsersRead);
        admin.MapGet("/users/{id}", async (string id, IIdentityService service, CancellationToken ct) =>
        {
            var user = await service.GetUserAsync(id, ct);
            return user is null ? Results.NotFound() : Results.Ok(user);
        }).RequireAuthorization(IdentityPermissions.UsersRead);
        admin.MapPut("/users/{id}/roles", async (string id, SetRolesRequest request, IIdentityAdministration service, CancellationToken ct) =>
            Respond(await service.SetRolesAsync(id, request.Roles, ct))).RequireAuthorization(IdentityPermissions.UsersWrite);
        admin.MapPut("/users/{id}/enabled", async (string id, SetUserEnabledRequest request, IIdentityAdministration service, CancellationToken ct) =>
            Respond(await service.SetEnabledAsync(id, request.Enabled, ct))).RequireAuthorization(IdentityPermissions.UsersWrite);
        admin.MapPost("/users/{id}/revoke-sessions", async (string id, IIdentityService service, CancellationToken ct) =>
        {
            await service.RevokeSessionsAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(IdentityPermissions.UsersWrite);
        admin.MapGet("/roles", async (IIdentityAdministration service, CancellationToken ct) =>
            Results.Ok(await service.GetRolesAsync(ct))).RequireAuthorization(IdentityPermissions.RolesRead);
        admin.MapPost("/roles", async (CreateRoleRequest request, IIdentityAdministration service, CancellationToken ct) =>
            Respond(await service.CreateRoleAsync(request, ct))).RequireAuthorization(IdentityPermissions.RolesWrite);
        admin.MapPut("/roles/{id}/permissions", async (string id, SetPermissionsRequest request, IIdentityAdministration service, CancellationToken ct) =>
            Respond(await service.SetPermissionsAsync(id, request.Permissions, ct))).RequireAuthorization(IdentityPermissions.RolesWrite);
        admin.MapDelete("/roles/{id}", async (string id, IIdentityAdministration service, CancellationToken ct) =>
            Respond(await service.DeleteRoleAsync(id, ct))).RequireAuthorization(IdentityPermissions.RolesWrite);
        admin.MapGet("/permissions", (IOptions<IdpOptions> options) =>
            Results.Ok(IdentityPermissions.All.Concat(options.Value.PermissionCatalog).Distinct(StringComparer.Ordinal)))
            .RequireAuthorization(IdentityPermissions.RolesRead);
        return endpoints;
    }

    private static IResult Respond<T>(IdentityResult<T> result)
    {
        if (result.Succeeded) return Results.Ok(result.Value);
        var status = result.Errors[0].Code switch
        {
            "InvalidCredentials" => StatusCodes.Status401Unauthorized,
            "TooManyRequests" => StatusCodes.Status429TooManyRequests,
            "SmsUnavailable" => StatusCodes.Status503ServiceUnavailable,
            "NotFound" => StatusCodes.Status404NotFound,
            "Conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(new { errors = result.Errors }, statusCode: status);
    }
}
