using IdentityProvider.Application.IRepository.Base;
using Microsoft.EntityFrameworkCore;

namespace IdentityProvider.Infrastructure.DataBase.Repository.Base;

/// <summary>EF Core implementation of the generic repository contract.</summary>
public sealed class GenericRepository<TEntity>(DbContext dbContext)
    : IGenericRepository<TEntity> where TEntity : class
{
    private DbSet<TEntity> Set => dbContext.Set<TEntity>();

    public IQueryable<TEntity> Query() => Set;

    public ValueTask<TEntity> FindAsync(params object[] keyValues) => Set.FindAsync(keyValues);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await Set.AddAsync(entity, cancellationToken);
    }

    public void Update(TEntity entity) => Set.Update(entity);

    public void Remove(TEntity entity) => Set.Remove(entity);
}
