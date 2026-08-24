namespace Ordering.Domain.Orders;

/// <summary>
/// One row per state transition, appended in the same transaction as the
/// status update and the outbox event. Never written outside Order.
/// </summary>
public sealed class StatusHistoryEntry
{
    private StatusHistoryEntry()
    {
        // EF Core materialization.
    }

    internal StatusHistoryEntry(Guid orderId, OrderStatus? from, OrderStatus to, Actor actor, DateTimeOffset at, string? reason)
    {
        OrderId = orderId;
        From = from;
        To = to;
        Actor = actor;
        At = at;
        Reason = reason;
    }

    public long Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? From { get; private set; }
    public OrderStatus To { get; private set; }
    public Actor Actor { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string? Reason { get; private set; }
}
