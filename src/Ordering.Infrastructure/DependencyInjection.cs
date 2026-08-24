using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Infrastructure.Payments;
using Ordering.Infrastructure.Persistence;
using Ordering.Infrastructure.Projections;
using Ordering.Infrastructure.ReadModel;
using Ordering.Infrastructure.Workers;

namespace Ordering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Ordering")
            ?? throw new InvalidOperationException("ConnectionStrings__Ordering is not configured.");

        services.AddDbContext<OrderingDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        // Dapper reads the projection tables over its own connections; the
        // read side never goes through the DbContext.
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());
        services.AddScoped<IOrderWriteRepository, OrderWriteRepository>();
        services.AddScoped<ITransactionManager, EfTransactionManager>();
        services.AddSingleton<IUniqueViolationDetector, NpgsqlUniqueViolationDetector>();

        services.AddSingleton<SimulatedPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<SimulatedPaymentGateway>());

        services.AddScoped<IRestaurantReadRepository, RestaurantReadRepository>();
        services.AddScoped<IOrderReadRepository, OrderReadRepository>();

        services.AddSingleton<OutboxProjectionProcessor>();
        services.AddSingleton<ExpiryProcessor>();
        services.AddSingleton<AcceptanceTimeoutProcessor>();
        services.AddSingleton<RefundProcessor>();

        return services;
    }

    /// <summary>
    /// The hosted loops are registered separately so integration tests can run
    /// the processors deterministically without background polling.
    /// </summary>
    public static IServiceCollection AddOrderingWorkers(this IServiceCollection services)
    {
        services.AddHostedService<OutboxProjectorService>();
        services.AddHostedService<ExpiryWorkerService>();
        services.AddHostedService<AcceptanceTimeoutWorkerService>();
        services.AddHostedService<RefundWorkerService>();
        return services;
    }
}
