using Ordering.Domain;
using Ordering.Domain.Catalog;
using Ordering.Domain.Orders;

namespace Ordering.Application.Abstractions;

/// <summary>
/// Write-model access for command handlers. Loads of orders that will be
/// transitioned take a row lock (FOR UPDATE) so concurrent transitions on the
/// same order serialize instead of racing.
/// </summary>
public interface IOrderWriteRepository
{
    Task<Restaurant?> GetRestaurantWithMenuAsync(Guid restaurantId, CancellationToken cancellationToken);

    void Add(Order order);

    Task<Order?> GetForUpdateAsync(Guid orderId, CancellationToken cancellationToken);

    Task<Order?> FindByIdempotencyKeyAsync(string customerId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Cumulative spend for the guardrail: totals of the customer's orders
    /// created at or after <paramref name="since"/>, excluding cancelled,
    /// expired, and refunded orders (money that never settled or came back).
    /// </summary>
    Task<Money> GetSpendSinceAsync(string customerId, DateTimeOffset since, Guid? excludeOrderId, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Opened by the TransactionBehavior around command handlers only.</summary>
public interface ITransactionManager
{
    Task<TResponse> InTransactionAsync<TResponse>(Func<Task<TResponse>> action, CancellationToken cancellationToken);
}

/// <summary>Detects provider-specific unique-constraint violations without leaking Npgsql into the application layer.</summary>
public interface IUniqueViolationDetector
{
    bool IsIdempotencyKeyViolation(Exception exception);
}
