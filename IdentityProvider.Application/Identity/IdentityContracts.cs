namespace IdentityProvider.Application.Identity;

// Provider-neutral contracts: no ASP.NET Identity, EF or token-provider types escape Infrastructure.
public record RegisterRequest(string UserName, string Password);
public record PasswordLoginRequest(string UserName, string Password);
public record SmsRequest(string PhoneNumber);
public record SmsLoginRequest(string PhoneNumber, string Code);
public record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string TokenType = "Bearer");
public record UserInfo(string Id, string UserName, string PhoneNumber, bool PhoneConfirmed, bool Enabled, string[] Roles);
public record RoleInfo(string Id, string Name, string[] Permissions);
public record CreateRoleRequest(string Name, string[] Permissions);
public record SetRolesRequest(string[] Roles);
public record SetPermissionsRequest(string[] Permissions);
public record SetUserEnabledRequest(bool Enabled);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record SetPasswordRequest(string NewPassword);
public record IdentityFailure(string Code, string Description);
public record IdentityResult<T>(T Value, IdentityFailure[] Errors)
{
    public bool Succeeded => Errors.Length == 0;
    public static IdentityResult<T> Success(T value) => new(value, []);
    public static IdentityResult<T> Fail(string code, string description) => new(default, [new(code, description)]);
}

public interface IIdentityService
{
    Task<IdentityResult<UserInfo>> AuthenticatePasswordAsync(PasswordLoginRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<UserInfo>> AuthenticateSmsAsync(SmsLoginRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<UserInfo>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<TokenResponse>> LoginAsync(PasswordLoginRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> RequestSmsAsync(SmsRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<TokenResponse>> LoginSmsAsync(SmsLoginRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> RequestPhoneVerificationAsync(string id, SmsRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> VerifyPhoneAsync(string id, SmsLoginRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> SetPasswordAsync(string id, SetPasswordRequest request, CancellationToken cancellationToken);
    Task<UserInfo> GetUserAsync(string id, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> ChangePasswordAsync(string id, ChangePasswordRequest request, CancellationToken cancellationToken);
    Task RevokeSessionsAsync(string id, CancellationToken cancellationToken);
}

public interface IIdentityAdministration
{
    Task<UserInfo[]> GetUsersAsync(int skip, int take, CancellationToken cancellationToken);
    Task<RoleInfo[]> GetRolesAsync(CancellationToken cancellationToken);
    Task<IdentityResult<RoleInfo>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> SetPermissionsAsync(string roleId, string[] permissions, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> SetRolesAsync(string userId, string[] roles, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken);
    Task<IdentityResult<bool>> DeleteRoleAsync(string roleId, CancellationToken cancellationToken);
}

public interface ISmsSender
{
    Task SendLoginCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken);
}

public static class IdentityPermissions
{
    public const string ClaimType = "permission";
    public const string UsersRead = "identity.users.read";
    public const string UsersWrite = "identity.users.write";
    public const string RolesRead = "identity.roles.read";
    public const string RolesWrite = "identity.roles.write";
    public static readonly string[] All = [UsersRead, UsersWrite, RolesRead, RolesWrite];
}
