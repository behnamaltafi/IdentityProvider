using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using IdentityProvider.Infrastructure.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IdentityProvider.Infrastructure.Identity.OpenIdConnect;

public static class ClientRegistration
{
    public static async Task RegisterIdentityProviderClientsAsync(this IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        await using var scope = provider.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<IdpOptions>>().Value;
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        if (settings.Scopes.Any(item => string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Resource)))
            throw new InvalidOperationException("Each Idp:Scopes entry needs a Name and Resource.");
        var allowedScopes = settings.Scopes.Select(x => x.Name).Concat([Scopes.OpenId, Scopes.Profile, Scopes.Phone, Scopes.Email, Scopes.OfflineAccess]).ToHashSet(StringComparer.Ordinal);
        // Validate every client before mutating registrations.
        foreach (var item in settings.Clients)
        {
            if (string.IsNullOrWhiteSpace(item.ClientId) || item.Flow is not ("Interactive" or "Service" or "Resource")
                || item.Scopes.Any(value => !allowedScopes.Contains(value)))
                throw new InvalidOperationException("Invalid Idp client or unknown scope.");
            if (item.Flow is "Service" or "Resource" && (item.ClientSecret.Length < 32 || item.Scopes.Any(value => value is Scopes.OpenId or Scopes.Profile or Scopes.Phone or Scopes.Email or Scopes.OfflineAccess)))
                throw new InvalidOperationException("Service clients need a secret of 32+ characters and API scopes only.");
            if (item.Flow == "Interactive" && (item.RedirectUris.Length == 0 || (item.ClientSecret.Length is > 0 and < 32)))
                throw new InvalidOperationException("Interactive clients need redirect URIs and an optional secret of 32+ characters.");
            foreach (var address in item.RedirectUris.Concat(item.PostLogoutRedirectUris))
            {
                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment)
                    || uri.Scheme == "http" && !uri.IsLoopback || uri.Scheme is "file" or "javascript" or "data")
                    throw new InvalidOperationException("Use exact HTTPS, loopback HTTP or native application redirect URIs without fragments.");
            }
        }
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in settings.Scopes)
        {
            var descriptor = new OpenIddictScopeDescriptor { Name = item.Name, DisplayName = item.DisplayName };
            descriptor.Resources.Add(item.Resource);
            var existing = await scopes.FindByNameAsync(item.Name, cancellationToken);
            if (existing is null) await scopes.CreateAsync(descriptor, cancellationToken);
            else await scopes.UpdateAsync(existing, descriptor, cancellationToken);
        }
        foreach (var item in settings.Clients)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = item.ClientId, DisplayName = item.DisplayName,
                ClientType = string.IsNullOrEmpty(item.ClientSecret) ? ClientTypes.Public : ClientTypes.Confidential,
                ClientSecret = string.IsNullOrEmpty(item.ClientSecret) ? null : item.ClientSecret,
                // These are operator-registered first-party clients; third-party consent isn't implicit registration.
                ConsentType = ConsentTypes.Implicit
            };
            if (item.Flow == "Resource") descriptor.Permissions.Add(Permissions.Endpoints.Introspection);
            else descriptor.Permissions.UnionWith([Permissions.Endpoints.Token, Permissions.Endpoints.Revocation]);
            if (item.Flow == "Interactive")
            {
                descriptor.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code]);
                descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            }
            else if (item.Flow == "Service") descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
            foreach (var value in item.Scopes.Where(value => value is not (Scopes.OpenId or Scopes.OfflineAccess)))
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + value);
            foreach (var uri in item.RedirectUris) descriptor.RedirectUris.Add(new Uri(uri));
            foreach (var uri in item.PostLogoutRedirectUris) descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
            var existing = await applications.FindByClientIdAsync(item.ClientId, cancellationToken);
            if (existing is null) await applications.CreateAsync(descriptor, cancellationToken);
            else await applications.UpdateAsync(existing, descriptor, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
