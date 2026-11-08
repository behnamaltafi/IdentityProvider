using System.Net;
using System.Text.Json;
using IdentityProvider.Infrastructure.IntegrationTests.Support;

namespace IdentityProvider.Infrastructure.IntegrationTests.Api;

/// <summary>Confirms that the API starts with a public liveness check and working Swagger.</summary>
public sealed class ApiSmokeTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ApiSmokeTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        using var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SwaggerDocument_IsAvailableAndValid()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(content);
        Assert.True(document.RootElement.GetProperty("openapi").GetString()?.StartsWith("3.", StringComparison.Ordinal));
    }
}
