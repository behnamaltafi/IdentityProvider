using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IdentityProvider.Infrastructure.Identity.OpenIdConnect;

public sealed class SessionRevoker(IOpenIddictTokenManager tokens)
{
    public async Task RevokeAsync(string userId, CancellationToken cancellationToken)
    {
        var active = new List<object>();
        await foreach (var token in tokens.FindBySubjectAsync(userId, cancellationToken))
            if (await tokens.HasStatusAsync(token, Statuses.Valid, cancellationToken)) active.Add(token);
        foreach (var token in active)
            if (!await tokens.TryRevokeAsync(token, cancellationToken))
                throw new InvalidOperationException("Token revocation conflicted with another update; retry.");
    }
}
