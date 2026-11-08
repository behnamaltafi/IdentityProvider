using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IdentityProvider.Application.Identity;
using IdentityProvider.Infrastructure.Identity.Persistence;
using IdentityProvider.Infrastructure.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityProvider.Infrastructure.IntegrationTests.Identity;

public sealed class IdentityFlowTests
{
    private const string Password = "Customer-password-123!";
    private const string Phone = "09123456789";

    [Fact]
    public async Task PasswordRegistration_Login_Me_AndLogoutRevokeToken()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        var user = await RegisterAsync(client);
        Assert.Empty(user.Roles);
        await LoginAsync(client);
        Assert.Equal(user.Id, (await client.GetFromJsonAsync<UserInfo>("/api/identity/me"))!.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/identity/logout-all", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/me")).StatusCode);
    }

    [Fact]
    public async Task AnonymousAndOrdinaryUsersCannotManageIdentity()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/admin/users")).StatusCode);
        await RegisterAsync(client);
        await LoginAsync(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/identity/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/identity/admin/roles", new CreateRoleRequest("Escalation", IdentityPermissions.All))).StatusCode);
    }

    [Fact]
    public async Task RoleGrants_WorkAndPermissionRemovalAppliesToExistingToken()
    {
        await using var factory = new IdentityApiFactory();
        using var admin = await factory.ReadyAsync();
        var user = await RegisterAsync(admin);
        await LoginAsync(admin, "testadmin", IdentityApiFactory.AdminPassword);
        var response = await admin.PostAsJsonAsync("/api/identity/admin/roles", new CreateRoleRequest("Reader", [IdentityPermissions.UsersRead]));
        response.EnsureSuccessStatusCode();
        var role = (await response.Content.ReadFromJsonAsync<RoleInfo>())!;
        (await admin.PutAsJsonAsync($"/api/identity/admin/users/{user.Id}/roles", new SetRolesRequest([role.Name]))).EnsureSuccessStatusCode();
        using var reader = factory.CreateClient();
        await LoginAsync(reader);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/identity/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/identity/admin/roles")).StatusCode);
        (await admin.PutAsJsonAsync($"/api/identity/admin/roles/{role.Id}/permissions", new SetPermissionsRequest([]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/identity/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/identity/admin/roles/{role.Id}")).StatusCode);
        (await admin.PutAsJsonAsync($"/api/identity/admin/users/{user.Id}/roles", new SetRolesRequest([]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await reader.GetAsync("/api/identity/me")).StatusCode);
        (await admin.DeleteAsync($"/api/identity/admin/roles/{role.Id}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DisableThenEnable_DoesNotResurrectOldSessions()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        var user = await RegisterAsync(client);
        await LoginAsync(client);
        using var admin = factory.CreateClient();
        await LoginAsync(admin, "testadmin", IdentityApiFactory.AdminPassword);
        (await admin.PutAsJsonAsync($"/api/identity/admin/users/{user.Id}/enabled", new SetUserEnabledRequest(false))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/login", new PasswordLoginRequest("customer", Password))).StatusCode);
        (await admin.PutAsJsonAsync($"/api/identity/admin/users/{user.Id}/enabled", new SetUserEnabledRequest(true))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/me")).StatusCode);
        await LoginAsync(client);
    }

    [Fact]
    public async Task SmsRegistersThenLogsIntoSameAccount_AndRejectsReplay()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        var request = await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone));
        request.EnsureSuccessStatusCode();
        Assert.DoesNotContain(factory.Sms.Code, await request.Content.ReadAsStringAsync());
        Assert.Equal("+989123456789", factory.Sms.Phone);
        using (var scope = factory.Services.CreateScope())
        {
            var challenge = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SmsChallenges.SingleAsync();
            Assert.NotEqual(factory.Sms.Code, challenge.CodeHash);
            Assert.Equal(64, challenge.CodeHash.Length);
        }
        await VerifySmsAsync(client, factory.Sms.Code);
        var first = (await client.GetFromJsonAsync<UserInfo>("/api/identity/me"))!;
        Assert.True(first.PhoneConfirmed);
        Assert.Empty(first.Roles);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/sms/verify", new SmsLoginRequest(Phone, factory.Sms.Code))).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest("+989123456789"))).EnsureSuccessStatusCode();
        await VerifySmsAsync(client, factory.Sms.Code);
        Assert.Equal(first.Id, (await client.GetFromJsonAsync<UserInfo>("/api/identity/me"))!.Id);
    }

    [Fact]
    public async Task SmsWrongAttemptDoesNotInvalidateCorrectCode_ButAttemptLimitDoes()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone))).EnsureSuccessStatusCode();
        var wrongCode = factory.Sms.Code == "000000" ? "111111" : "000000";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/sms/verify", new SmsLoginRequest(Phone, wrongCode))).StatusCode);
        await VerifySmsAsync(client, factory.Sms.Code);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone))).EnsureSuccessStatusCode();
        wrongCode = factory.Sms.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/sms/verify", new SmsLoginRequest(Phone, wrongCode))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/sms/verify", new SmsLoginRequest(Phone, factory.Sms.Code))).StatusCode);
    }

    [Fact]
    public async Task SmsExpiry_ResendCooldown_AndDeliveryFailureAreEnforced()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone))).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(121));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/sms/verify", new SmsLoginRequest(Phone, factory.Sms.Code))).StatusCode);
        factory.Sms.Fail = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone))).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SmsChallenges.SingleAsync()).ExpiresAt <= factory.Clock.GetUtcNow());
    }

    [Fact]
    public async Task VerifiedPhoneLinksToPasswordAccount_AndBindingCodeCannotBeUsedForLogin()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        var user = await RegisterAsync(client);
        await LoginAsync(client);
        (await client.PostAsJsonAsync("/api/identity/phone/request", new SmsRequest(Phone))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/sms/verify", new SmsLoginRequest(Phone, factory.Sms.Code))).StatusCode);
        (await client.PostAsJsonAsync("/api/identity/phone/verify", new SmsLoginRequest(Phone, factory.Sms.Code))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/me")).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone))).EnsureSuccessStatusCode();
        await VerifySmsAsync(client, factory.Sms.Code);
        Assert.Equal(user.Id, (await client.GetFromJsonAsync<UserInfo>("/api/identity/me"))!.Id);
    }

    [Fact]
    public async Task SmsAccountCanSetPassword_AndPasswordChangeRevokesExistingTokens()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone))).EnsureSuccessStatusCode();
        await VerifySmsAsync(client, factory.Sms.Code);
        var user = (await client.GetFromJsonAsync<UserInfo>("/api/identity/me"))!;
        (await client.PostAsJsonAsync("/api/identity/password/set", new SetPasswordRequest(Password))).EnsureSuccessStatusCode();
        await LoginAsync(client, user.UserName);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/identity/password/set", new SetPasswordRequest("Other-password-123!"))).StatusCode);
        (await client.PostAsJsonAsync("/api/identity/password", new ChangePasswordRequest(Password, "Changed-password-123!"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/me")).StatusCode);
        await LoginAsync(client, user.UserName, "Changed-password-123!");
    }

    [Fact]
    public async Task PasswordLockoutRejectsCorrectPasswordAfterFiveFailures()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        await RegisterAsync(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/login", new PasswordLoginRequest("customer", "wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/login", new PasswordLoginRequest("customer", Password))).StatusCode);
    }

    [Fact]
    public async Task InvalidPermissionsAndWeakPasswordsAreRejected()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/identity/register", new RegisterRequest("customer", "weak"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/identity/register", new RegisterRequest("sms_989123456789", Password))).StatusCode);
        await LoginAsync(client, "testadmin", IdentityApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/identity/admin/roles", new CreateRoleRequest("Unknown", ["unknown.permission"]))).StatusCode);
    }

    [Fact]
    public async Task TamperedPayloadIsRejected()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        await RegisterAsync(client);
        var token = await LoginAsync(client);
        var parts = token.AccessToken.Split('.');
        var json = System.Text.Encoding.UTF8.GetString(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1]));
        parts[1] = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(json.Replace("identityprovider-api", "other-api", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Authorization = new("Bearer", string.Join('.', parts));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/me")).StatusCode);
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("expired")]
    public async Task SignedTokensWithInvalidBoundariesAreRejected(string invalid)
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        await RegisterAsync(client);
        var valid = await LoginAsync(client);
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var original = handler.ReadJwtToken(valid.AccessToken);
        var settings = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityProvider.Infrastructure.Identity.IdentityModuleOptions>>().Value;
        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(settings.SigningKeyPem);
        var signing = new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa), "RS256")
        {
            CryptoProviderFactory = new() { CacheSignatureProviders = false }
        };
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            invalid == "issuer" ? "https://wrong.test" : settings.Issuer,
            invalid == "audience" ? "wrong-api" : settings.Audience,
            original.Claims.Where(c => c.Type is not ("aud" or "iss" or "exp" or "nbf")),
            now.AddMinutes(-5), invalid == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5), signing);
        client.DefaultRequestHeaders.Authorization = new("Bearer", handler.WriteToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/me")).StatusCode);
    }

    [Fact]
    public async Task StaleConcurrentOtpConsumerCannotReuseCode()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest(Phone))).EnsureSuccessStatusCode();
        using var firstScope = factory.Services.CreateScope();
        using var staleScope = factory.Services.CreateScope();
        await firstScope.ServiceProvider.GetRequiredService<IdentityDbContext>().SmsChallenges.LoadAsync();
        await staleScope.ServiceProvider.GetRequiredService<IdentityDbContext>().SmsChallenges.LoadAsync();
        var request = new SmsLoginRequest(Phone, factory.Sms.Code);
        Assert.True((await firstScope.ServiceProvider.GetRequiredService<IIdentityService>().LoginSmsAsync(request, default)).Succeeded);
        Assert.False((await staleScope.ServiceProvider.GetRequiredService<IIdentityService>().LoginSmsAsync(request, default)).Succeeded);
        Assert.Equal(2, await firstScope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task SmsRequestsAreRateLimitedAcrossDifferentPhones()
    {
        await using var factory = new IdentityApiFactory();
        using var client = await factory.ReadyAsync();
        for (var i = 0; i < 5; i++)
            (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest($"+98912345678{i}"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/identity/sms/request", new SmsRequest("+989123456799"))).StatusCode);
    }

    [Fact]
    public async Task ExternalModeDoesNotExposeLocalIdentityEndpointsOrRegisterLocalPersistence()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        // Anonymous request to an unmapped route is caught by the authenticated fallback policy.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/identity/register", new RegisterRequest("customer", Password))).StatusCode);
        Assert.False(factory.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IIdentityService)));
        Assert.False(factory.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IdentityDbContext)));
        var dataSource = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>();
        Assert.DoesNotContain(dataSource.Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/identity", StringComparison.Ordinal) == true);
        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        var granted = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(IdentityPermissions.ClaimType, IdentityPermissions.UsersRead)], "External"));
        Assert.True((await authorization.AuthorizeAsync(granted, null, IdentityPermissions.UsersRead)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(granted, null, IdentityPermissions.UsersWrite)).Succeeded);
    }

    private static async Task<UserInfo> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/identity/register", new RegisterRequest("customer", Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserInfo>())!;
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string userName = "customer", string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/identity/login", new PasswordLoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return token;
    }

    private static async Task VerifySmsAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/identity/sms/verify", new SmsLoginRequest(Phone, code));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
    }
}
