using System.Net.Http.Json;
using IdentityProvider.Application.Identity;
using Microsoft.Extensions.Options;

namespace IdentityProvider.Infrastructure.Identity.Sms;

// Gateway contract: HTTPS POST { phoneNumber, code }, Authorization: Bearer <key>.
// Implement ISmsSender for a vendor-specific template API without changing login code.
public sealed class HttpSmsSender(HttpClient client, IOptions<SmsGatewayOptions> options) : ISmsSender
{
    public async Task SendLoginCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https"
            || string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Configure an HTTPS Sms:Endpoint and Sms:ApiKey or register ISmsSender.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new("Bearer", settings.ApiKey);
        request.Content = JsonContent.Create(new { phoneNumber, code });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
