using System.Security.Cryptography;
using IdentityProvider.Infrastructure.Identity.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IdentityProvider.Infrastructure.Identity.OpenIdConnect;

public static class OpenIdConnectModule
{
    public const string BrowserScheme = "Idp.Browser";
    public const string CorsPolicy = "Idp.Clients";

    public static IServiceCollection AddIdentityProviderServer(this IServiceCollection services, IConfiguration configuration)
    {
        var identity = configuration.GetSection("Identity").Get<IdentityModuleOptions>() ?? new();
        if (identity.Provider != "Local") return services;
        var settings = configuration.GetSection("Idp").Get<IdpOptions>() ?? new();
        if (settings.RefreshTokenDays is < 1 or > 90)
            throw new InvalidOperationException("Idp:RefreshTokenDays must be between 1 and 90.");
        if (settings.PermissionCatalog.Any(value => value is null || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z][a-z0-9_.:-]{0,127}$")))
            throw new InvalidOperationException("Idp:PermissionCatalog entries must be bounded lower-case permission identifiers.");
        var encryption = Convert.FromBase64String(settings.EncryptionKey);
        if (encryption.Length != 32) throw new InvalidOperationException("Idp:EncryptionKey must be a base64 256-bit key.");
        services.Configure<IdpOptions>(configuration.GetSection("Idp"));
        services.AddScoped<IdpPrincipalFactory>();
        services.AddScoped<SessionRevoker>();
        services.AddAuthentication().AddCookie(BrowserScheme, options =>
        {
            options.Cookie.Name = "__Host-IdentityProvider";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.LoginPath = "/account/login";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<IdentityUserAccount>>();
                var userId = context.Principal?.FindFirst(Claims.Subject)?.Value;
                var user = userId is null ? null : await manager.FindByIdAsync(userId);
                if (user is null || !user.Enabled || await manager.IsLockedOutAsync(user)
                    || context.Principal.FindFirst("session_version")?.Value != SessionVersion.Create(user.SecurityStamp, identity.OtpHashKey))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(BrowserScheme);
                }
            };
        });
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            if (settings.AllowedOrigins.Length != 0)
                policy.WithOrigins(settings.AllowedOrigins).WithMethods("GET", "POST", "OPTIONS")
                    .WithHeaders("Authorization", "Content-Type");
        }));
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<IdentityDbContext>())
            .AddServer(options =>
            {
                options.SetIssuer(new Uri(identity.Issuer));
                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetEndSessionEndpointUris("connect/logout")
                    .SetRevocationEndpointUris("connect/revoke")
                    .SetIntrospectionEndpointUris("connect/introspect");
                options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow().AllowClientCredentialsFlow();
                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Phone, Scopes.Email, Scopes.OfflineAccess);
                options.RegisterScopes(settings.Scopes.Select(scope => scope.Name).ToArray());
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(identity.AccessTokenMinutes));
                options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(5));
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(settings.RefreshTokenDays));
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                options.Configure(configuration => configuration.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));
                using var rsa = RSA.Create();
                rsa.ImportFromPem(identity.SigningKeyPem);
                var key = new RsaSecurityKey(rsa.ExportParameters(true));
                key.KeyId = Base64UrlEncoder.Encode(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
                options.AddSigningKey(key);
                options.AddEncryptionKey(new SymmetricSecurityKey(encryption)
                {
                    KeyId = Base64UrlEncoder.Encode(SHA256.HashData(encryption))
                });
                // Resource servers can verify access JWTs using discovery/JWKS without private keys.
                // Codes and refresh tokens remain encrypted and persisted by OpenIddict.
                options.DisableAccessTokenEncryption();
                options.UseAspNetCore().EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough().EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
                options.EnableTokenEntryValidation();
            });
        return services;
    }
}
