using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StockLab.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<StockLabDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("StockLab");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:StockLab is required to use persistence.");
            }

            options.UseSqlServer(connectionString);
        });

        return services;
    }
}
