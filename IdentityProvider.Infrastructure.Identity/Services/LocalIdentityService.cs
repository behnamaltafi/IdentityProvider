using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using IdentityProvider.Application.Identity;
using IdentityProvider.Infrastructure.Identity.Persistence;
using IdentityProvider.Infrastructure.Identity.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IdentityProvider.Infrastructure.Identity.Services;

public sealed class LocalIdentityService(
    UserManager<IdentityUserAccount> users,
    RoleManager<IdentityRole> roles,
    IdentityDbContext db,
    ISmsSender sms,
    IOptions<IdentityModuleOptions> options,
    TimeProvider clock,
    SessionRevoker sessions) : IIdentityService
{
    private readonly IdentityModuleOptions settings = options.Value;

    public async Task<IdentityResult<UserInfo>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        // The sms_ namespace is reserved for verified phone registrations.
        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length > 128
            || request.UserName.StartsWith("sms_", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
            return IdentityResult<UserInfo>.Fail("InvalidRegistration", "Invalid user name or password.");
        var user = new IdentityUserAccount { UserName = request.UserName };
        var result = await users.CreateAsync(user, request.Password);
        // Do not disclose whether a user name already exists.
        return result.Succeeded
            ? IdentityResult<UserInfo>.Success(await DescribeAsync(user))
            : IdentityResult<UserInfo>.Fail("InvalidRegistration", "Registration failed. Use a unique user name and a password of at least 12 characters with upper/lower case, digits and symbols.");
    }

    public async Task<IdentityResult<UserInfo>> AuthenticatePasswordAsync(PasswordLoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
            return InvalidUserLogin();
        var user = await users.FindByNameAsync(request.UserName);
        if (user is null || !user.Enabled || await users.IsLockedOutAsync(user))
            return InvalidUserLogin();
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return InvalidUserLogin();
        }
        await users.ResetAccessFailedCountAsync(user);
        return IdentityResult<UserInfo>.Success(await DescribeAsync(user));
    }

    public async Task<IdentityResult<TokenResponse>> LoginAsync(PasswordLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await AuthenticatePasswordAsync(request, cancellationToken);
        if (!result.Succeeded) return InvalidLogin();
        return IdentityResult<TokenResponse>.Success(await IssueTokenAsync(await users.FindByIdAsync(result.Value.Id)));
    }

    public static string NormalizePhone(string input)
    {
        if (input is null || input.Length > 32) return null;
        var phone = input.Trim();
        if (Regex.IsMatch(phone, "^09[0-9]{9}$", RegexOptions.CultureInvariant))
            phone = "+98" + phone[1..];
        return Regex.IsMatch(phone, "^\\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant) ? phone : null;
    }

    public Task<IdentityResult<bool>> RequestSmsAsync(SmsRequest request, CancellationToken cancellationToken) =>
        SendChallengeAsync(request, null, cancellationToken);

    public async Task<IdentityResult<bool>> RequestPhoneVerificationAsync(string id, SmsRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null || !user.Enabled) return IdentityResult<bool>.Fail("NotFound", "User not found.");
        // Binding is explicit and separate from anonymous login: never link by an unverified profile number.
        return await SendChallengeAsync(request, id, cancellationToken);
    }

    private async Task<IdentityResult<bool>> SendChallengeAsync(SmsRequest request, string subjectId, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.PhoneNumber);
        if (phone is null) return IdentityResult<bool>.Fail("InvalidPhone", "Use an E.164 phone number or an Iranian 09 mobile number.");
        var now = clock.GetUtcNow();
        var challenge = await db.SmsChallenges.FindAsync([phone], cancellationToken);
        if (challenge is not null && now < challenge.SentAt.AddSeconds(settings.OtpResendSeconds))
            return IdentityResult<bool>.Fail("TooManyRequests", "Please wait before requesting another code.");
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        if (challenge is null)
        {
            challenge = new SmsChallenge { PhoneNumber = phone };
            db.SmsChallenges.Add(challenge);
        }
        challenge.Version = Guid.NewGuid().ToString();
        challenge.Salt = Guid.NewGuid().ToString();
        challenge.CodeHash = HashCode(phone, code, challenge.Salt);
        challenge.SentAt = now;
        challenge.ExpiresAt = now.AddSeconds(settings.OtpLifetimeSeconds);
        challenge.Attempts = 0;
        challenge.SubjectId = subjectId;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return IdentityResult<bool>.Fail("TooManyRequests", "Please retry later."); }
        try { await sms.SendLoginCodeAsync(phone, code, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Invalidate an undelivered code; never log it or return it to callers.
            challenge.ExpiresAt = now;
            challenge.Version = Guid.NewGuid().ToString();
            try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { }
            return IdentityResult<bool>.Fail("SmsUnavailable", "SMS delivery is currently unavailable.");
        }
        return IdentityResult<bool>.Success(true);
    }

    public async Task<IdentityResult<UserInfo>> AuthenticateSmsAsync(SmsLoginRequest request, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.PhoneNumber);
        if (!await ConsumeChallengeAsync(request, null, cancellationToken)) return InvalidUserLogin();
        var user = await users.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone, cancellationToken);
        if (user is null)
        {
            user = new IdentityUserAccount { UserName = "sms_" + phone[1..], PhoneNumber = phone, PhoneNumberConfirmed = true };
            var result = await users.CreateAsync(user);
            if (!result.Succeeded) return InvalidUserLogin();
        }
        if (!user.Enabled || !user.PhoneNumberConfirmed || await users.IsLockedOutAsync(user)) return InvalidUserLogin();
        return IdentityResult<UserInfo>.Success(await DescribeAsync(user));
    }

    public async Task<IdentityResult<TokenResponse>> LoginSmsAsync(SmsLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await AuthenticateSmsAsync(request, cancellationToken);
        if (!result.Succeeded) return InvalidLogin();
        return IdentityResult<TokenResponse>.Success(await IssueTokenAsync(await users.FindByIdAsync(result.Value.Id)));
    }

    public async Task<IdentityResult<bool>> VerifyPhoneAsync(string id, SmsLoginRequest request, CancellationToken cancellationToken)
    {
        if (!await ConsumeChallengeAsync(request, id, cancellationToken))
            return IdentityResult<bool>.Fail("InvalidCredentials", "Invalid or expired credentials.");
        var user = await users.FindByIdAsync(id);
        var phone = NormalizePhone(request.PhoneNumber);
        if (user is null || !user.Enabled || await users.Users.AnyAsync(x => x.PhoneNumber == phone && x.Id != id, cancellationToken))
            return IdentityResult<bool>.Fail("PhoneUnavailable", "Phone number cannot be linked.");
        user.PhoneNumber = phone;
        user.PhoneNumberConfirmed = true;
        try
        {
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded)
                return IdentityResult<bool>.Fail("Conflict", "User update conflicted; retry.");
            await sessions.RevokeAsync(id, cancellationToken);
            return IdentityResult<bool>.Success(true);
        }
        catch (DbUpdateException) { return IdentityResult<bool>.Fail("PhoneUnavailable", "Phone number cannot be linked."); }
    }

    private async Task<bool> ConsumeChallengeAsync(SmsLoginRequest request, string subjectId, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.PhoneNumber);
        if (phone is null || request.Code is null || !Regex.IsMatch(request.Code, "^[0-9]{6}$")) return false;
        var challenge = await db.SmsChallenges.FindAsync([phone], cancellationToken);
        if (challenge is null || challenge.SubjectId != subjectId || challenge.ExpiresAt <= clock.GetUtcNow() || challenge.Attempts >= settings.OtpMaxAttempts)
            return false;
        var valid = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(challenge.CodeHash), Convert.FromHexString(HashCode(phone, request.Code, challenge.Salt)));
        challenge.Attempts++;
        if (valid) challenge.ExpiresAt = clock.GetUtcNow();
        // The stable salt preserves the code across failed attempts; Version guards atomic updates.
        challenge.Version = Guid.NewGuid().ToString();
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return false; }
        return valid;
    }

    public async Task<IdentityResult<bool>> SetPasswordAsync(string id, SetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null || string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length > 256 || await users.HasPasswordAsync(user))
            return IdentityResult<bool>.Fail("InvalidPassword", "Cannot set password; use password change for an existing password.");
        var result = await users.AddPasswordAsync(user, request.NewPassword);
        if (result.Succeeded) await sessions.RevokeAsync(id, cancellationToken);
        return result.Succeeded ? IdentityResult<bool>.Success(true) : IdentityResult<bool>.Fail("InvalidPassword", "Password does not meet policy.");
    }

    private string HashCode(string phone, string code, string salt) => Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.OtpHashKey), Encoding.UTF8.GetBytes(phone + ":" + code + ":" + salt)));

    public async Task<UserInfo> GetUserAsync(string id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        return user is null ? null : await DescribeAsync(user);
    }

    private async Task<UserInfo> DescribeAsync(IdentityUserAccount user) => new(
        user.Id, user.UserName, user.PhoneNumber, user.PhoneNumberConfirmed, user.Enabled, (await users.GetRolesAsync(user)).ToArray());

    public async Task<IdentityResult<bool>> ChangePasswordAsync(string id, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null || string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword)
            || request.CurrentPassword.Length > 256 || request.NewPassword.Length > 256)
            return IdentityResult<bool>.Fail("InvalidPassword", "Password change failed.");
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (result.Succeeded) await sessions.RevokeAsync(id, cancellationToken);
        return result.Succeeded ? IdentityResult<bool>.Success(true) : IdentityResult<bool>.Fail("InvalidPassword", "Password change failed.");
    }

    public async Task RevokeSessionsAsync(string id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is not null && !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            throw new InvalidOperationException("Session revocation conflicted with another account update; retry.");
        await sessions.RevokeAsync(id, cancellationToken);
    }

    private async Task<TokenResponse> IssueTokenAsync(IdentityUserAccount user)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id), new("name", user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("session_version", SessionVersion.Create(await users.GetSecurityStampAsync(user), settings.OtpHashKey))
        };
        foreach (var name in await users.GetRolesAsync(user))
        {
            claims.Add(new Claim("role", name));
            var role = await roles.FindByNameAsync(name);
            if (role is not null)
                claims.AddRange((await roles.GetClaimsAsync(role)).Where(c => c.Type == IdentityPermissions.ClaimType));
        }
        using var rsa = RSA.Create();
        rsa.ImportFromPem(settings.SigningKeyPem);
        // RSA is scoped to this call; a cached signature provider must not retain the disposed key.
        var signing = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims.DistinctBy(c => (c.Type, c.Value)),
            now.UtcDateTime, expires.UtcDateTime, signing);
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static IdentityResult<TokenResponse> InvalidLogin() => IdentityResult<TokenResponse>.Fail("InvalidCredentials", "Invalid or expired credentials.");
    private static IdentityResult<UserInfo> InvalidUserLogin() => IdentityResult<UserInfo>.Fail("InvalidCredentials", "Invalid or expired credentials.");
}
