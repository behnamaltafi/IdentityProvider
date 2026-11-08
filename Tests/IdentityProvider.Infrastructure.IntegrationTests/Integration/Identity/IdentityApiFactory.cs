using System.Security.Cryptography;
using IdentityProvider.Application.Identity;
using IdentityProvider.Infrastructure.Identity;
using IdentityProvider.Infrastructure.Identity.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using IdentityProvider.Infrastructure.Identity.OpenIdConnect;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IdentityProvider.Infrastructure.IntegrationTests.Identity;

public sealed class IdentityApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string signingKey;
    private readonly bool enableDirectLogin;
    public CapturingSmsSender Sms { get; } = new();
    public MutableClock Clock { get; } = new();
    public const string AdminPassword = "Admin-test-password-123!";

    public IdentityApiFactory(bool enableDirectLogin = true)
    {
        this.enableDirectLogin = enableDirectLogin;
        using var rsa = RSA.Create(2048);
        signingKey = rsa.ExportRSAPrivateKeyPem();
        connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var configuration = new Dictionary<string, string?>
        {
            ["Identity:Provider"] = "Local",
            ["Identity:EnableDirectLogin"] = enableDirectLogin.ToString(),
            ["Identity:Issuer"] = "https://identity.test",
            ["Identity:Audience"] = "identityprovider-api",
            ["Identity:SigningKeyPem"] = signingKey,
            ["Idp:EncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Idp:Scopes:0:Name"] = "identity.api",
            ["Idp:Scopes:0:Resource"] = "identityprovider-api",
            ["Idp:Scopes:1:Name"] = "bonakmarket.api",
            ["Idp:Scopes:1:Resource"] = "bonakmarket-api",
            ["Idp:Clients:0:ClientId"] = "test-web",
            ["Idp:Clients:0:Flow"] = "Interactive",
            ["Idp:Clients:0:RedirectUris:0"] = "https://client.test/callback",
            ["Idp:Clients:0:PostLogoutRedirectUris:0"] = "https://client.test/",
            ["Idp:Clients:0:Scopes:0"] = "openid",
            ["Idp:Clients:0:Scopes:1"] = "profile",
            ["Idp:Clients:0:Scopes:2"] = "phone",
            ["Idp:Clients:0:Scopes:3"] = "offline_access",
            ["Idp:Clients:0:Scopes:4"] = "identity.api",
            ["Idp:Clients:0:Scopes:5"] = "bonakmarket.api",
            ["Idp:Clients:1:ClientId"] = "test-worker",
            ["Idp:Clients:1:Flow"] = "Service",
            ["Idp:Clients:1:ClientSecret"] = "test-only-client-secret-32-characters-long!",
            ["Idp:Clients:1:Scopes:0"] = "bonakmarket.api",
            ["Idp:Clients:2:ClientId"] = "bonakmarket-api",
            ["Idp:Clients:2:Flow"] = "Resource",
            ["Idp:Clients:2:ClientSecret"] = "test-only-resource-secret-32-characters-long!",
            ["Idp:Clients:2:Scopes:0"] = "bonakmarket.api",
            ["Idp:AllowedOrigins:0"] = "http://localhost:5173",
            ["Idp:PermissionCatalog:0"] = "bonakmarket.orders.read",
            ["Identity:OtpHashKey"] = "test-only-otp-hmac-key-at-least-32-characters",
            ["Identity:BootstrapUserName"] = "testadmin",
            ["Identity:BootstrapPassword"] = AdminPassword,
            ["ConnectionStrings:IdentityDbContext"] = "Server=unused;Database=unused"
        };
        foreach (var (key, value) in configuration) builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(connection));
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            // IdentityModel normally uses wall time; align it with the test's OTP/token clock.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
                    expires.HasValue && expires.Value >= Clock.GetUtcNow().UtcDateTime - parameters.ClockSkew
                    && (!notBefore.HasValue || notBefore.Value <= Clock.GetUtcNow().UtcDateTime + parameters.ClockSkew));
        });
    }

    public async Task<HttpClient> ReadyAsync()
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.EnsureCreatedAsync();
        await Services.InitializeIdentityAsync();
        await Services.RegisterIdentityProviderClientsAsync();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}

public sealed class CapturingSmsSender : ISmsSender
{
    public string Phone { get; private set; } = "";
    public string Code { get; private set; } = "";
    public bool Fail { get; set; }
    public Task SendLoginCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        if (Fail) throw new HttpRequestException("Test delivery failure");
        Phone = phoneNumber;
        Code = code;
        return Task.CompletedTask;
    }
}

public sealed class MutableClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now = now.Add(duration);
}
