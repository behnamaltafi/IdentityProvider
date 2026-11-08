using System.Security.Cryptography;
using System.Threading.RateLimiting;
using IdentityProvider.Application.Identity;
using IdentityProvider.Infrastructure.Identity.Authorization;
using IdentityProvider.Infrastructure.Identity.Persistence;
using IdentityProvider.Infrastructure.Identity.Services;
using IdentityProvider.Infrastructure.Identity.Sms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IdentityProvider.Infrastructure.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Identity").Get<IdentityModuleOptions>() ?? new();
        if (settings.Provider is not ("Local" or "External"))
            throw new InvalidOperationException("Identity:Provider must be Local or External.");
        services.Configure<IdentityModuleOptions>(configuration.GetSection("Identity"));
        services.TryAddSingleton(TimeProvider.System);
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var permission in IdentityPermissions.All)
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("identity-auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("identity-sms", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
        });
        var authentication = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
        if (settings.Provider == "External")
        {
            authentication.AddJwtBearer(options =>
            {
                options.Authority = configuration["Authentication:Authority"];
                options.Audience = configuration["Authentication:Audience"];
                options.RequireHttpsMetadata = configuration.GetValue("Authentication:RequireHttpsMetadata", true);
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
            });
            services.AddScoped<IAuthorizationHandler, ExternalPermissionHandler>();
            return services;
        }

        ValidateLocalSettings(settings);
        settings.Issuer = new Uri(settings.Issuer).AbsoluteUri;
        services.PostConfigure<IdentityModuleOptions>(value => value.Issuer = new Uri(value.Issuer).AbsoluteUri);
        var connectionString = configuration.GetConnectionString("IdentityDbContext");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("ConnectionStrings:IdentityDbContext is required.");
        services.AddDbContext<IdentityDbContext>(options =>
        {
            if (settings.DatabaseProvider == "Sqlite") options.UseSqlite(connectionString);
            else if (settings.DatabaseProvider == "SqlServer") options.UseSqlServer(connectionString);
            else throw new InvalidOperationException("Identity:DatabaseProvider must be SqlServer or Sqlite.");
            options.UseOpenIddict();
        });
        services.AddIdentityCore<IdentityUserAccount>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<IdentityDbContext>();
        services.AddScoped<IIdentityService, LocalIdentityService>();
        services.AddScoped<IIdentityAdministration, LocalIdentityAdministration>();
        services.AddScoped<IAuthorizationHandler, LocalPermissionHandler>();
        services.Configure<SmsGatewayOptions>(configuration.GetSection("Sms"));
        services.AddHttpClient<HttpSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.TryAddScoped<ISmsSender>(provider => provider.GetRequiredService<HttpSmsSender>());
        authentication.AddJwtBearer(options =>
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(settings.SigningKeyPem);
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = settings.Issuer,
                ValidateAudience = true, ValidAudience = settings.Audience,
                ValidateLifetime = true, RequireExpirationTime = true,
                ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromSeconds(15), NameClaimType = "name", RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<IdentityUserAccount>>();
                    var id = context.Principal?.FindFirst("sub")?.Value;
                    var user = id is null ? null : await manager.FindByIdAsync(id);
                    if (user is null || !user.Enabled || await manager.IsLockedOutAsync(user)
                        || context.Principal?.FindFirst("session_version")?.Value != SessionVersion.Create(user.SecurityStamp, settings.OtpHashKey))
                        context.Fail("Session is no longer valid.");
                }
            };
        });
        return services;
    }

    private static void ValidateLocalSettings(IdentityModuleOptions settings)
    {
        if (settings.OtpHashKey.Length < 32 || settings.AccessTokenMinutes is < 1 or > 60
            || settings.OtpLifetimeSeconds is < 30 or > 300 || settings.OtpResendSeconds < 30
            || settings.OtpMaxAttempts is < 1 or > 10 || string.IsNullOrWhiteSpace(settings.Audience)
            || !Uri.TryCreate(settings.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme != "https")
            throw new InvalidOperationException("Invalid local identity settings. Use HTTPS issuer, audience, a secret OTP key (32+ chars) and bounded token/OTP lifetimes.");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(settings.SigningKeyPem);
        if (rsa.KeySize < 2048 || rsa.ExportParameters(true).D is null)
            throw new InvalidOperationException("Identity:SigningKeyPem must contain a private RSA key of at least 2048 bits.");
    }

    // Explicit operator step, after applying migrations. Never executed on normal API startup.
    public static async Task InitializeIdentityAsync(this IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        await using var scope = provider.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<IdentityModuleOptions>>().Value;
        if (settings.Provider != "Local") return;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUserAccount>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (string.IsNullOrWhiteSpace(settings.BootstrapUserName) || string.IsNullOrWhiteSpace(settings.BootstrapPassword))
            throw new InvalidOperationException("Supply Identity:BootstrapUserName and Identity:BootstrapPassword for initialization.");
        // Refuse to elevate an existing self-registered account with a matching name.
        if (await users.FindByNameAsync(settings.BootstrapUserName) is not null)
            throw new InvalidOperationException("Bootstrap user already exists. Initialization is a one-time operator action.");
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var role = await roles.FindByNameAsync("IdentityAdministrator");
        if (role is null)
        {
            role = new IdentityRole("IdentityAdministrator");
            Ensure(await roles.CreateAsync(role));
        }
        var existing = await roles.GetClaimsAsync(role);
        foreach (var permission in IdentityPermissions.All)
            if (!existing.Any(c => c.Type == IdentityPermissions.ClaimType && c.Value == permission))
                Ensure(await roles.AddClaimAsync(role, new(IdentityPermissions.ClaimType, permission)));
        var admin = new IdentityUserAccount { UserName = settings.BootstrapUserName };
        Ensure(await users.CreateAsync(admin, settings.BootstrapPassword));
        Ensure(await users.AddToRoleAsync(admin, role.Name));
        await transaction.CommitAsync(cancellationToken);
    }

    private static void Ensure(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
