namespace IdentityProvider.Infrastructure.Identity;

public sealed class IdentityModuleOptions
{
    public string Provider { get; set; } = "Local";
    public bool EnableDirectLogin { get; set; }
    public string DatabaseProvider { get; set; } = "SqlServer";
    public string Issuer { get; set; } = "https://localhost:7100/";
    public string Audience { get; set; } = "identityprovider-api";
    // PEM RSA private key supplied through secrets/environment; never commit it.
    public string SigningKeyPem { get; set; } = "";
    public string OtpHashKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int OtpLifetimeSeconds { get; set; } = 120;
    public int OtpResendSeconds { get; set; } = 60;
    public int OtpMaxAttempts { get; set; } = 5;
    public string BootstrapUserName { get; set; } = "";
    public string BootstrapPassword { get; set; } = "";
}

public sealed class SmsGatewayOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
