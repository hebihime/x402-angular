using Ordering.Domain.Orders;

namespace Ordering.Domain.Events;

/// <summary>
/// Domain events raised by the Order aggregate. They are persisted to the
/// Outbox table in the same transaction as the state change that raised them
/// (never emitted anywhere else) and drained by the projector.
/// Each event carries a full denormalized snapshot of the order so the
/// projector never has to read the write model.
/// </summary>
public interface IDomainEvent
{
    Guid OrderId { get; }
    DateTimeOffset OccurredAt { get; }
}

public sealed record OrderSnapshot(
    Guid OrderId,
    Guid RestaurantId,
    string CustomerId,
    OrderStatus Status,
    Money Total,
    IReadOnlyList<OrderLine> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int RefundAttempts,
    string? LastRefundError,
    bool ManualInterventionRequired);

public sealed record HistoryDelta(
    OrderStatus? From,
    OrderStatus To,
    Actor Actor,
    DateTimeOffset At,
    string? Reason);

public sealed record OrderPlaced(OrderSnapshot Order, HistoryDelta History, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid OrderId => Order.OrderId;
}

public sealed record OrderStatusChanged(OrderSnapshot Order, HistoryDelta History, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid OrderId => Order.OrderId;
}

public sealed record RefundAttemptFailed(OrderSnapshot Order, int Attempt, string Error, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid OrderId => Order.OrderId;
}
