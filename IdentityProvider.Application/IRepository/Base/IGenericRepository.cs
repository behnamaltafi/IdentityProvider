namespace IdentityProvider.Application.IRepository.Base;

/// <summary>Basic persistence operations shared by application entities.</summary>
public interface IGenericRepository<TEntity> where TEntity : class
{
    IQueryable<TEntity> Query();
    ValueTask<TEntity> FindAsync(params object[] keyValues);
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
}
