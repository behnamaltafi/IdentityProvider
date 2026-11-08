namespace IdentityProvider.Infrastructure.Identity.OpenIdConnect;

public sealed class IdpOptions
{
    public string EncryptionKey { get; set; } = "";
    public int RefreshTokenDays { get; set; } = 14;
    public List<IdpScope> Scopes { get; set; } = [];
    public List<IdpClient> Clients { get; set; } = [];
    public string[] AllowedOrigins { get; set; } = [];
    public string[] PermissionCatalog { get; set; } = [];
}

public sealed class IdpScope
{
    public string Name { get; set; } = "";
    public string Resource { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

// Client registration is an explicit operator command, never a public self-registration API.
public sealed class IdpClient
{
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Flow { get; set; } = "Interactive";
    public string ClientSecret { get; set; } = "";
    public string[] RedirectUris { get; set; } = [];
    public string[] PostLogoutRedirectUris { get; set; } = [];
    public string[] Scopes { get; set; } = [];
}
