using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Abstractions;
using Ordering.Application.Behaviors;
using Ordering.Application.Common;
using Ordering.Application.Orders;
using Ordering.Application.Orders.Commands;

namespace Ordering.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderingApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);

            // The pipeline order is deliberate: log everything, reject bad
            // input before touching the database, resolve idempotent replays
            // before opening a transaction, and only then wrap the handler in
            // the transaction that makes transitions atomic.
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(IdempotencyBehavior<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Open-generic validators are not picked up by assembly scanning.
        services.AddScoped<IValidator<AcceptOrderCommand>, RestaurantTransitionValidator<AcceptOrderCommand>>();
        services.AddScoped<IValidator<RejectOrderCommand>, RestaurantTransitionValidator<RejectOrderCommand>>();
        services.AddScoped<IValidator<StartPreparingCommand>, RestaurantTransitionValidator<StartPreparingCommand>>();
        services.AddScoped<IValidator<MarkReadyCommand>, RestaurantTransitionValidator<MarkReadyCommand>>();
        services.AddScoped<IValidator<CompleteOrderCommand>, RestaurantTransitionValidator<CompleteOrderCommand>>();

        services.AddScoped<IIdempotencyReplayer<PlaceOrderCommand, Result<OrderDto>>, PlaceOrderReplayer>();

        return services;
    }
}
