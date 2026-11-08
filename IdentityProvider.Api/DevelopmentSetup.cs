using System.Security.Cryptography;
using System.Text.Json;

namespace IdentityProvider.Api;

internal static class DevelopmentSetup
{
    public static void Prepare(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "appsettings.Development.Local.json");
        if (File.Exists(path))
        {
            Console.WriteLine("Development settings already exist; existing keys were preserved.");
            return;
        }
        var dataDirectory = Path.GetFullPath(Path.Combine(contentRoot, "..", "data"));
        Directory.CreateDirectory(dataDirectory);
        using var rsa = RSA.Create(3072);
        var settings = new
        {
            Identity = new
            {
                Provider = "Local", DatabaseProvider = "Sqlite", Issuer = "https://localhost:7100/",
                Audience = "identityprovider-api", SigningKeyPem = rsa.ExportRSAPrivateKeyPem(),
                OtpHashKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            },
            ConnectionStrings = new { IdentityDbContext = $"Data Source={Path.Combine(dataDirectory, "identity.db")}" },
            Idp = new
            {
                EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                AllowedOrigins = new[] { "http://localhost:5173" },
                Scopes = new[]
                {
                    new { Name = "identity.api", Resource = "identityprovider-api", DisplayName = "Identity management API" },
                    new { Name = "bonakmarket.api", Resource = "bonakmarket-api", DisplayName = "BonakMarket API" }
                },
                Clients = new object[]
                {
                    new { ClientId = "web-app", DisplayName = "Web application", Flow = "Interactive", RedirectUris = new[] { "http://localhost:5173/auth/callback" },
                        PostLogoutRedirectUris = new[] { "http://localhost:5173/" }, Scopes = new[] { "openid", "profile", "phone", "offline_access", "identity.api", "bonakmarket.api" } },
                    new { ClientId = "mobile-app", DisplayName = "Mobile application", Flow = "Interactive", RedirectUris = new[] { "com.identityprovider.app:/oauth/callback" },
                        Scopes = new[] { "openid", "profile", "phone", "offline_access", "bonakmarket.api" } },
                    new { ClientId = "service-worker", DisplayName = "Microservice worker", Flow = "Service", ClientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                        Scopes = new[] { "bonakmarket.api" } }
                }
            }
        };
        // CreateNew prevents overwriting another setup process's keys.
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("Development settings created. No secrets were printed. Keep appsettings.Development.Local.json private.");
    }
}
