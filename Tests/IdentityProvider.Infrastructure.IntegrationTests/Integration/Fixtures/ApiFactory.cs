using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace IdentityProvider.Infrastructure.IntegrationTests.Support;

/// <summary>Starts the API with test-safe authentication settings and no external services.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Identity:Provider", "External");
        builder.ConfigureAppConfiguration(configurationBuilder =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceName"] = "IdentityProvider.IntegrationTests",
                ["Authentication:Authority"] = "https://issuer.example.test",
                ["Authentication:Audience"] = "template-api-tests",
                ["Authentication:RequireHttpsMetadata"] = "true"
            });
        });
    }
}
