using IdentityProvider.Application.IRepository.Base;
using IdentityProvider.Infrastructure.DataBase;
using IdentityProvider.Infrastructure.DataBase.Repository.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityProvider.Infrastructure.Extensions;

/// <summary>Registers persistence services used by the application.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ApplicationDbContext");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString);
            }
        });
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
