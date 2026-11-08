using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdentityProvider.Infrastructure.Identity.Persistence;

public sealed class IdentityDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__IdentityDbContext");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set ConnectionStrings__IdentityDbContext before running identity migrations.");
        return new(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connectionString).Options);
    }
}
