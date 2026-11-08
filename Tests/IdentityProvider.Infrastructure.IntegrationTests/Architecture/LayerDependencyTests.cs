using IdentityProvider.Application;
using IdentityProvider.Domain;

namespace IdentityProvider.Infrastructure.IntegrationTests.Architecture;

/// <summary>
/// Guards the dependency direction of the Clean Architecture layering.
/// </summary>
public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_DoesNotReferenceApplicationInfrastructureOrApi()
    {
        var references = ReferencedAssemblyNames(typeof(DomainAssembly).Assembly);

        Assert.DoesNotContain(references, name =>
            name.Contains("Application", StringComparison.Ordinal)
            || name.Contains("Infrastructure", StringComparison.Ordinal)
            || name.EndsWith(".Api", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureOrApi()
    {
        var references = ReferencedAssemblyNames(typeof(ApplicationAssembly).Assembly);

        Assert.DoesNotContain(references, name =>
            name.Contains("Infrastructure", StringComparison.Ordinal)
            || name.EndsWith(".Api", StringComparison.Ordinal));
    }

    private static IReadOnlyCollection<string> ReferencedAssemblyNames(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
}
