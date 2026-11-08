using IdentityProvider.Application.IRepository.Base;
using Microsoft.EntityFrameworkCore;

namespace IdentityProvider.Infrastructure.DataBase.Repository.Base;

/// <summary>EF Core implementation of the unit of work contract.</summary>
public sealed class UnitOfWork(DbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
