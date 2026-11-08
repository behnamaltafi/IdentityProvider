using IdentityProvider.Infrastructure.DataBase.Repository.Base;
using IdentityProvider.Infrastructure.IntegrationTests.Support;

namespace IdentityProvider.Infrastructure.IntegrationTests.DataBase.Repository.Base;

/// <summary>Exercises the generic repository and unit of work with EF Core InMemory.</summary>
public sealed class GenericRepositoryTests
{
    [Fact]
    public async Task AddAndFindAsync_ReturnsThePersistedEntity()
    {
        await using var context = TestDbContextFactory.Create();
        var repository = new GenericRepository<TestEntity>(context);
        var unitOfWork = new UnitOfWork(context);
        var entity = new TestEntity { Id = 101, Title = "sample" };

        await repository.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();

        var result = await repository.FindAsync(101L);

        Assert.NotNull(result);
        Assert.Equal("sample", result.Title);
    }

    [Fact]
    public async Task UpdateAndRemove_ChangeTheQueryableSet()
    {
        await using var context = TestDbContextFactory.Create();
        var repository = new GenericRepository<TestEntity>(context);
        var unitOfWork = new UnitOfWork(context);
        var entity = new TestEntity { Id = 202, Title = "before" };

        await repository.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();

        entity.Title = "after";
        repository.Update(entity);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal("after", repository.Query().Single().Title);

        repository.Remove(entity);
        await unitOfWork.SaveChangesAsync();

        Assert.Empty(repository.Query());
    }
}
