namespace IdentityProvider.Application.IRepository.Base;

/// <summary>Commits pending persistence changes as one unit.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
