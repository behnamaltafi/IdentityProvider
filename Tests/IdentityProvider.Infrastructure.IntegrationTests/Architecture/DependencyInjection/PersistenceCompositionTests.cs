using IdentityProvider.Application.IRepository.Base;
using IdentityProvider.Infrastructure.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityProvider.Infrastructure.IntegrationTests.Architecture.DependencyInjection;

/// <summary>Checks that the template registers its generic persistence contracts.</summary>
public sealed class PersistenceCompositionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PersistenceCompositionTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void GenericRepositoryAndUnitOfWork_AreRegistered()
    {
        var serviceProbe = _factory.Services.GetRequiredService<IServiceProviderIsService>();

        Assert.True(serviceProbe.IsService(typeof(IGenericRepository<TestEntity>)));
        Assert.True(serviceProbe.IsService(typeof(IUnitOfWork)));
    }
}
