using Microsoft.EntityFrameworkCore;
using Ordering.Application.Abstractions;
using Ordering.Domain;
using Ordering.Domain.Catalog;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure.Persistence;

public sealed class OrderWriteRepository(OrderingDbContext dbContext) : IOrderWriteRepository
{
    /// <summary>Statuses whose money never settled or came back; excluded from the spend guardrail.</summary>
    private static readonly OrderStatus[] NonSpendStatuses =
    [
        OrderStatus.Cancelled,
        OrderStatus.Expired,
        OrderStatus.Refunded,
    ];

    public Task<Restaurant?> GetRestaurantWithMenuAsync(Guid restaurantId, CancellationToken cancellationToken) =>
        dbContext.Restaurants
            .AsNoTracking()
            .Include(r => r.MenuItems)
            .ThenInclude(mi => mi.ModifierGroups)
            .ThenInclude(g => g.Modifiers)
            .SingleOrDefaultAsync(r => r.Id == restaurantId, cancellationToken);

    public void Add(Order order) => dbContext.Orders.Add(order);

    /// <summary>
    /// Loads with a row lock so concurrent transitions on the same order
    /// serialize; combined with the transaction the behavior opened, the state
    /// machine check always sees the latest committed status.
    /// </summary>
    public Task<Order?> GetForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.Orders
            .FromSqlInterpolated($"SELECT * FROM orders WHERE id = {orderId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Order?> FindByIdempotencyKeyAsync(string customerId, string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.CustomerId == customerId && o.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<Money> GetSpendSinceAsync(string customerId, DateTimeOffset since, Guid? excludeOrderId, CancellationToken cancellationToken)
    {
        var totals = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId
                && o.CreatedAt >= since
                && !NonSpendStatuses.Contains(o.Status)
                && (excludeOrderId == null || o.Id != excludeOrderId))
            .Select(o => o.Total)
            .ToListAsync(cancellationToken);

        var sum = Money.Zero;
        foreach (var total in totals)
        {
            sum += total;
        }

        return sum;
    }
}
