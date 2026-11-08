using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdentityProvider.Application.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace IdentityProvider.Infrastructure.IntegrationTests.Identity;

public sealed class OpenIdConnectTests
{
    private const string RedirectUri = "https://client.test/callback";
    private const string WorkerSecret = "test-only-client-secret-32-characters-long!";
    private const string DefaultScopes = "openid profile offline_access identity.api bonakmarket.api";

    [Fact]
    public async Task DiscoveryAndJwksArePublicAndAdvertiseOnlySupportedFlows()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        using var metadata = await JsonAsync(await browser.GetAsync("/.well-known/openid-configuration"));
        Assert.Equal("https://identity.test/", metadata.RootElement.GetProperty("issuer").GetString());
        Assert.Equal("https://identity.test/connect/authorize", metadata.RootElement.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://identity.test/connect/token", metadata.RootElement.GetProperty("token_endpoint").GetString());
        var methods = metadata.RootElement.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(["S256"], methods);
        var grants = metadata.RootElement.GetProperty("grant_types_supported").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("authorization_code", grants);
        Assert.Contains("refresh_token", grants);
        Assert.Contains("client_credentials", grants);
        Assert.DoesNotContain("password", grants);
        using var jwks = await JsonAsync(await browser.GetAsync(metadata.RootElement.GetProperty("jwks_uri").GetString()));
        var key = jwks.RootElement.GetProperty("keys")[0];
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.False(key.TryGetProperty("d", out _));
        Assert.False(key.TryGetProperty("p", out _));
    }

    [Fact]
    public async Task PkceLoginIssuesExternallyVerifiableTokensAndAuthenticatedUserInfo()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        var grant = await AuthorizeAsync(browser);
        using var token = await ExchangeAsync(browser, grant);
        var access = token.RootElement.GetProperty("access_token").GetString()!;
        var idToken = token.RootElement.GetProperty("id_token").GetString()!;
        Assert.True(token.RootElement.TryGetProperty("refresh_token", out _));
        using var metadata = await JsonAsync(await browser.GetAsync("/.well-known/openid-configuration"));
        var jwks = await browser.GetStringAsync(metadata.RootElement.GetProperty("jwks_uri").GetString());
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = "https://identity.test/", ValidAudience = "bonakmarket-api",
            IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
            ValidateIssuerSigningKey = true, ValidateLifetime = true, ValidAlgorithms = ["RS256"]
        };
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var user = handler.ValidateToken(access, parameters, out _);
        Assert.Equal("testadmin", user.FindFirst("name")?.Value);
        Assert.Contains(user.Claims, c => c.Type == IdentityPermissions.ClaimType && c.Value == IdentityPermissions.UsersRead);
        Assert.DoesNotContain(user.Claims, c => c.Type.Contains("security_stamp", StringComparison.Ordinal));
        parameters.ValidAudience = "test-web";
        var identity = handler.ValidateToken(idToken, parameters, out _);
        Assert.Equal(grant.Nonce, identity.FindFirst("nonce")?.Value);
        Assert.DoesNotContain(identity.Claims, c => c.Type == "session_version" || c.Type == IdentityPermissions.ClaimType);
        // Browser cookie alone cannot authorize bearer-only administration APIs.
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/identity/admin/users")).StatusCode);
        browser.DefaultRequestHeaders.Authorization = new("Bearer", access);
        using var info = await JsonAsync(await browser.GetAsync("/connect/userinfo"));
        Assert.Equal(user.FindFirst("sub")?.Value, info.RootElement.GetProperty("sub").GetString());
        Assert.Equal("testadmin", info.RootElement.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/identity/admin/users")).StatusCode);
    }

    [Fact]
    public async Task AuthorizationCodeCannotBeReplayedOrExchangedWithWrongVerifier()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        var first = await AuthorizeAsync(browser);
        using var issued = await ExchangeAsync(browser, first);
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeResponseAsync(browser, first)).StatusCode);
        var second = await AuthorizeAsync(browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeResponseAsync(browser, second with { Verifier = new string('z', 64) })).StatusCode);
    }

    [Fact]
    public async Task RefreshRotatesAndDoesNotAllowScopeEscalation()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        using var original = await ExchangeAsync(browser, await AuthorizeAsync(browser));
        var refresh = original.RootElement.GetProperty("refresh_token").GetString()!;
        using var escalation = await TokenAsync(browser, new() { ["grant_type"] = "refresh_token", ["client_id"] = "test-web", ["refresh_token"] = refresh, ["scope"] = DefaultScopes + " phone" });
        Assert.Equal(HttpStatusCode.BadRequest, escalation.StatusCode);
        using var next = await JsonAsync(await TokenAsync(browser, new() { ["grant_type"] = "refresh_token", ["client_id"] = "test-web", ["refresh_token"] = refresh }));
        Assert.NotEqual(refresh, next.RootElement.GetProperty("refresh_token").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(browser, new() { ["grant_type"] = "refresh_token", ["client_id"] = "test-web", ["refresh_token"] = refresh })).StatusCode);
    }

    [Fact]
    public async Task RevokedRefreshTokenCannotIssueNewTokens()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        using var original = await ExchangeAsync(browser, await AuthorizeAsync(browser));
        var refresh = original.RootElement.GetProperty("refresh_token").GetString()!;
        var response = await browser.PostAsync("/connect/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = "test-web", ["token"] = refresh, ["token_type_hint"] = "refresh_token" }));
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(browser, new() { ["grant_type"] = "refresh_token", ["client_id"] = "test-web", ["refresh_token"] = refresh })).StatusCode);
    }

    [Fact]
    public async Task LogoutAllInvalidatesBrowserCookieAndRefreshToken()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        using var original = await ExchangeAsync(browser, await AuthorizeAsync(browser));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(original.RootElement.GetProperty("access_token").GetString());
        var userId = token.Claims.Single(c => c.Type == "sub").Value;
        var introspectionRequest = new Dictionary<string, string>
        {
            ["client_id"] = "bonakmarket-api", ["client_secret"] = "test-only-resource-secret-32-characters-long!",
            ["token"] = original.RootElement.GetProperty("access_token").GetString()!
        };
        using var before = await JsonAsync(await browser.PostAsync("/connect/introspect", new FormUrlEncodedContent(introspectionRequest)));
        Assert.True(before.RootElement.GetProperty("active").GetBoolean());
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIdentityService>().RevokeSessionsAsync(userId, default);
        using var after = await JsonAsync(await browser.PostAsync("/connect/introspect", new FormUrlEncodedContent(introspectionRequest)));
        Assert.False(after.RootElement.GetProperty("active").GetBoolean());
        var response = await browser.GetAsync(AuthorizationPath(NewVerifier(), DefaultScopes) + "&prompt=none");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("login_required", response.Headers.Location!.Query);
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(browser, new()
        { ["grant_type"] = "refresh_token", ["client_id"] = "test-web", ["refresh_token"] = original.RootElement.GetProperty("refresh_token").GetString()! })).StatusCode);
    }

    [Fact]
    public async Task SwaggerExposesOidcAndManagementAndDirectLoginIsDisabledByDefault()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        using var swagger = await JsonAsync(await browser.GetAsync("/swagger/v1/swagger.json"));
        var paths = swagger.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/connect/token", out _));
        Assert.True(paths.TryGetProperty("/api/identity/admin/users", out _));
        Assert.False(paths.TryGetProperty("/api/identity/login", out _));
        Assert.False(paths.TryGetProperty("/api/identity/sms/verify", out _));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task CustomMicroservicePermissionsCanBeAssignedAndAppearInOidcAccessToken()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        using var first = await ExchangeAsync(browser, await AuthorizeAsync(browser));
        browser.DefaultRequestHeaders.Authorization = new("Bearer", first.RootElement.GetProperty("access_token").GetString());
        using var catalog = await JsonAsync(await browser.GetAsync("/api/identity/admin/permissions"));
        Assert.Contains(catalog.RootElement.EnumerateArray(), permission => permission.GetString() == "bonakmarket.orders.read");
        using var role = await JsonAsync(await browser.PostAsJsonAsync("/api/identity/admin/roles", new CreateRoleRequest("OrderReader", ["bonakmarket.orders.read"])));
        var firstJwt = new JwtSecurityTokenHandler().ReadJwtToken(first.RootElement.GetProperty("access_token").GetString());
        var userId = firstJwt.Claims.Single(c => c.Type == "sub").Value;
        (await browser.PutAsJsonAsync($"/api/identity/admin/users/{userId}/roles", new SetRolesRequest(["OrderReader"]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/connect/userinfo")).StatusCode);
        browser.DefaultRequestHeaders.Authorization = null;
        using var next = await ExchangeAsync(browser, await AuthorizeAsync(browser));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(next.RootElement.GetProperty("access_token").GetString());
        Assert.Contains(jwt.Claims, claim => claim.Type == "permission" && claim.Value == "bonakmarket.orders.read");
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == "permission" && claim.Value == IdentityPermissions.UsersWrite);
    }

    [Fact]
    public async Task ClientCredentialsHaveOnlyRegisteredApiAudienceAndNoUserPrivileges()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var client = Browser(factory);
        using var result = await JsonAsync(await TokenAsync(client, WorkerRequest()));
        Assert.False(result.RootElement.TryGetProperty("refresh_token", out _));
        Assert.False(result.RootElement.TryGetProperty("id_token", out _));
        var access = result.RootElement.GetProperty("access_token").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(access);
        Assert.Equal(["bonakmarket-api"], jwt.Audiences);
        Assert.Contains(jwt.Claims, c => c.Type == "sub" && c.Value == "client:test-worker");
        Assert.DoesNotContain(jwt.Claims, c => c.Type is "role" or "permission");
        client.DefaultRequestHeaders.Authorization = new("Bearer", access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/connect/userinfo")).StatusCode);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("scope")]
    public async Task InvalidClientSecretOrUnregisteredScopeIsRejected(string invalid)
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var client = Browser(factory);
        var request = WorkerRequest();
        request[invalid == "secret" ? "client_secret" : "scope"] = invalid == "secret" ? "wrong" : "identity.api";
        var response = await TokenAsync(client, request);
        Assert.False(response.IsSuccessStatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(invalid == "secret" ? "invalid_client" : "invalid_request", error.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RegisteredResourceCanIntrospectItsOwnAudience()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var client = Browser(factory);
        using var token = await JsonAsync(await TokenAsync(client, WorkerRequest()));
        using var introspection = await JsonAsync(await client.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "bonakmarket-api", ["client_secret"] = "test-only-resource-secret-32-characters-long!",
            ["token"] = token.RootElement.GetProperty("access_token").GetString()!
        })));
        Assert.True(introspection.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal("client:test-worker", introspection.RootElement.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task BrowserSmsVerificationCreatesAccountAndCompletesOidcFlow()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        var verifier = NewVerifier();
        var authorizePath = AuthorizationPath(verifier, "openid profile phone offline_access bonakmarket.api");
        var challenge = await browser.GetAsync(authorizePath);
        var loginPage = await browser.GetStringAsync(challenge.Headers.Location);
        var send = await browser.PostAsync("/account/send-code", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["PhoneNumber"] = "09123456789", ["ReturnUrl"] = authorizePath, ["__RequestVerificationToken"] = AntiForgery(loginPage) }));
        Assert.Equal(HttpStatusCode.Redirect, send.StatusCode);
        loginPage = await browser.GetStringAsync(send.Headers.Location);
        var login = await browser.PostAsync("/account/login?handler=Sms", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["PhoneNumber"] = "09123456789", ["Code"] = factory.Sms.Code, ["ReturnUrl"] = authorizePath, ["__RequestVerificationToken"] = AntiForgery(loginPage) }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var callback = await browser.GetAsync(login.Headers.Location);
        var code = QueryHelpers.ParseQuery(callback.Headers.Location!.Query)["code"].ToString();
        using var token = await ExchangeAsync(browser, new(code, verifier, "test-nonce"));
        browser.DefaultRequestHeaders.Authorization = new("Bearer", token.RootElement.GetProperty("access_token").GetString());
        using var info = await JsonAsync(await browser.GetAsync("/connect/userinfo"));
        Assert.Equal("+989123456789", info.RootElement.GetProperty("phone_number").GetString());
        Assert.True(info.RootElement.GetProperty("phone_number_verified").GetBoolean());
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("pkce")]
    public async Task UnsafeAuthorizationRequestsAreRejected(string invalid)
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        var request = AuthorizationPath(NewVerifier(), DefaultScopes);
        request = invalid == "redirect" ? request.Replace(Uri.EscapeDataString(RedirectUri), Uri.EscapeDataString("https://evil.test/callback"), StringComparison.Ordinal)
            : request.Replace("code_challenge_method=S256", "code_challenge_method=plain", StringComparison.Ordinal);
        var response = await browser.GetAsync(request);
        Assert.False(response.IsSuccessStatusCode);
        Assert.False(response.Headers.Location?.Host == "evil.test");
        Assert.DoesNotContain("/account/login", response.Headers.Location?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountAndLogoutFormsRequireAntiforgeryAndCannotRedirectOffSite()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/account/login?handler=Password", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["UserName"] = "testadmin", ["Password"] = IdentityApiFactory.AdminPassword }))).StatusCode);
        var page = await browser.GetStringAsync("/account/login?ReturnUrl=https://evil.test");
        var login = await browser.PostAsync("/account/login?handler=Password", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["UserName"] = "testadmin", ["Password"] = IdentityApiFactory.AdminPassword, ["ReturnUrl"] = "https://evil.test", ["__RequestVerificationToken"] = AntiForgery(page) }));
        Assert.Equal("/", login.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/connect/logout", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
    }

    [Fact]
    public async Task LogoutClearsBrowserSessionAndUsesRegisteredPostLogoutRedirect()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        using var token = await ExchangeAsync(browser, await AuthorizeAsync(browser));
        var uri = "/connect/logout?client_id=test-web&post_logout_redirect_uri=" + Uri.EscapeDataString("https://client.test/");
        var page = await browser.GetStringAsync(uri);
        var response = await browser.PostAsync("/connect/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = "test-web", ["post_logout_redirect_uri"] = "https://client.test/", ["__RequestVerificationToken"] = AntiForgery(page) }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://client.test/", response.Headers.Location!.ToString());
        var authorize = await browser.GetAsync(AuthorizationPath(NewVerifier(), DefaultScopes) + "&prompt=none");
        Assert.Contains("login_required", authorize.Headers.Location!.Query);
    }

    [Fact]
    public async Task HttpsIsRequiredAndCorsAllowsOnlyConfiguredOrigins()
    {
        await using var factory = new IdentityApiFactory(enableDirectLogin: false);
        using var seeded = await factory.ReadyAsync();
        using var browser = Browser(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await seeded.GetAsync("/.well-known/openid-configuration")).StatusCode);
        foreach (var origin in new[] { "http://localhost:5173", "https://evil.test" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/connect/token");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            using var response = await browser.SendAsync(request);
            Assert.Equal(origin == "http://localhost:5173", response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    private static HttpClient Browser(IdentityApiFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://identity.test"), AllowAutoRedirect = false, HandleCookies = true });
    private static string NewVerifier() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
    private static string AuthorizationPath(string verifier, string scopes) => QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
    {
        ["client_id"] = "test-web", ["redirect_uri"] = RedirectUri, ["response_type"] = "code", ["scope"] = scopes,
        ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
        ["code_challenge_method"] = "S256", ["state"] = "test-state", ["nonce"] = "test-nonce"
    });
    private static async Task<Grant> AuthorizeAsync(HttpClient browser)
    {
        var verifier = NewVerifier();
        var path = AuthorizationPath(verifier, DefaultScopes);
        var response = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        if (response.Headers.Location!.ToString().Contains("/account/login", StringComparison.Ordinal))
        {
            var html = await browser.GetStringAsync(response.Headers.Location);
            var login = await browser.PostAsync("/account/login?handler=Password", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserName"] = "testadmin", ["Password"] = IdentityApiFactory.AdminPassword,
                ["ReturnUrl"] = path, ["__RequestVerificationToken"] = AntiForgery(html)
            }));
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            response = await browser.GetAsync(login.Headers.Location);
        }
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("client.test", response.Headers.Location!.Host);
        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Assert.Equal("test-state", query["state"].ToString());
        Assert.True(query.ContainsKey("code"), response.Headers.Location.ToString());
        return new(query["code"].ToString(), verifier, "test-nonce");
    }
    private static string AntiForgery(string html)
    {
        var input = Regex.Matches(html, "<input[^>]*>").Select(match => match.Value)
            .First(tag => tag.Contains("name=\"__RequestVerificationToken\"", StringComparison.Ordinal));
        return WebUtility.HtmlDecode(Regex.Match(input, "value=\"([^\"]+)\"").Groups[1].Value);
    }
    private static Task<HttpResponseMessage> ExchangeResponseAsync(HttpClient client, Grant grant) => TokenAsync(client, new()
    { ["grant_type"] = "authorization_code", ["client_id"] = "test-web", ["code"] = grant.Code, ["code_verifier"] = grant.Verifier, ["redirect_uri"] = RedirectUri });
    private static async Task<JsonDocument> ExchangeAsync(HttpClient client, Grant grant) => await JsonAsync(await ExchangeResponseAsync(client, grant));
    private static Task<HttpResponseMessage> TokenAsync(HttpClient client, Dictionary<string, string> values) => client.PostAsync("/connect/token", new FormUrlEncodedContent(values));
    private static Dictionary<string, string> WorkerRequest() => new()
    { ["grant_type"] = "client_credentials", ["client_id"] = "test-worker", ["client_secret"] = WorkerSecret, ["scope"] = "bonakmarket.api" };
    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }
    private sealed record Grant(string Code, string Verifier, string Nonce);
}
