using Microsoft.EntityFrameworkCore;

namespace IdentityProvider.Infrastructure.DataBase;

/// <summary>
/// The application database starts with no entities or tables. Add entity sets
/// and configurations here as application requirements are introduced.
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
