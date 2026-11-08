using Microsoft.EntityFrameworkCore;

namespace IdentityProvider.Infrastructure.IntegrationTests.Support;

/// <summary>A neutral entity used to exercise the generic persistence layer.</summary>
internal sealed class TestEntity
{
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();
}

internal static class TestDbContextFactory
{
    public static TestDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"template-tests-{Guid.NewGuid():N}")
            .Options;

        return new TestDbContext(options);
    }
}
