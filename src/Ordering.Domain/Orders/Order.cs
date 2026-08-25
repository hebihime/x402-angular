using Ordering.Domain.Events;

namespace Ordering.Domain.Orders;

/// <summary>
/// The order aggregate. <see cref="TransitionTo"/> is the ONLY code path that
/// changes <see cref="Status"/>; it appends the status-history row and raises
/// the domain event that becomes the outbox row, so the three writes of every
/// transition are structural, not disciplined.
/// </summary>
public sealed class Order
{
    private readonly List<StatusHistoryEntry> _history = [];
    private readonly List<IDomainEvent> _pendingEvents = [];
    private List<OrderLine> _lines = [];

    private Order()
    {
        // EF Core materialization.
        CustomerId = null!;
        IdempotencyKey = null!;
    }

    private Order(Guid id, Guid restaurantId, string customerId, string idempotencyKey)
    {
        Id = id;
        RestaurantId = restaurantId;
        CustomerId = customerId;
        IdempotencyKey = idempotencyKey;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public string CustomerId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public OrderStatus Status { get; private set; }

    /// <summary>Locked at draft creation; never changes afterwards.</summary>
    public Money Total { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Timestamp of the last transition or refund attempt; the workers' scan cursor.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
    public string? ChargeId { get; private set; }

    /// <summary>Verified payer wallet, set exactly once at settlement.</summary>
    public string? PayerAddress { get; private set; }
    public string? RefundId { get; private set; }
    public int RefundAttempts { get; private set; }
    public string? LastRefundError { get; private set; }
    public DateTimeOffset? NextRefundAttemptAt { get; private set; }
    public bool ManualInterventionRequired { get; private set; }
    public IReadOnlyList<StatusHistoryEntry> History => _history;

    /// <summary>
    /// Creates a draft from server-repriced, snapshotted lines. The (none) →
    /// draft transition goes through the same table check as every other.
    /// </summary>
    public static Order Place(
        Guid id,
        Guid restaurantId,
        string customerId,
        string idempotencyKey,
        IReadOnlyList<OrderLine> repricedLines,
        Money lockedTotal,
        DateTimeOffset now,
        TimeSpan draftTtl)
    {
        if (!OrderStateMachine.IsAllowed(null, OrderStatus.Draft, Actor.Customer))
        {
            throw new InvalidOperationException("Order creation is not permitted by the state machine table.");
        }

        var order = new Order(id, restaurantId, customerId, idempotencyKey)
        {
            Status = OrderStatus.Draft,
            _lines = [.. repricedLines],
            Total = lockedTotal,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + draftTtl,
        };

        var delta = new HistoryDelta(null, OrderStatus.Draft, Actor.Customer, now, null);
        order._history.Add(new StatusHistoryEntry(id, null, OrderStatus.Draft, Actor.Customer, now, null));
        order._pendingEvents.Add(new OrderPlaced(order.Snapshot(), delta, now));
        return order;
    }

    /// <summary>
    /// The only way to change an order's status. Invalid or repeated
    /// transitions leave the order untouched and report Ignored.
    /// </summary>
    public TransitionResult TransitionTo(OrderStatus to, Actor actor, DateTimeOffset now, string? reason = null)
    {
        if (!OrderStateMachine.IsAllowed(Status, to, actor))
        {
            return TransitionResult.Ignored;
        }

        var from = Status;
        Status = to;
        UpdatedAt = now;
        var delta = new HistoryDelta(from, to, actor, now, reason);
        _history.Add(new StatusHistoryEntry(Id, from, to, actor, now, reason));
        _pendingEvents.Add(new OrderStatusChanged(Snapshot(), delta, now));
        return TransitionResult.Applied;
    }

    /// <summary>
    /// Records the verified payer. Not a status write — settlement then
    /// <see cref="Confirm"/> transitions draft → paid.
    /// </summary>
    public void AssignPayer(string payerAddress) => PayerAddress = payerAddress;

    /// <summary>Draft → paid after facilitator-verified settlement.</summary>
    public TransitionResult Confirm(string chargeId, DateTimeOffset now)
    {
        var result = TransitionTo(OrderStatus.Paid, Actor.System, now, "facilitator-verified settlement");
        if (result.Transitioned)
        {
            ChargeId = chargeId;
        }

        return result;
    }

    /// <summary>
    /// Paid → rejected (restaurant or acceptance-timeout), then immediately
    /// rejected → refund_pending by the system — the refund lifecycle starts
    /// automatically and atomically with the rejection.
    /// </summary>
    public TransitionResult Reject(Actor actor, DateTimeOffset now, string? reason = null)
    {
        var result = TransitionTo(OrderStatus.Rejected, actor, now, reason);
        if (result.Transitioned)
        {
            TransitionTo(OrderStatus.RefundPending, Actor.System, now, "automatic on rejection");
            NextRefundAttemptAt = now;
        }

        return result;
    }

    /// <summary>Refund_pending → refunded after a successful gateway refund.</summary>
    public TransitionResult RecordRefundSuccess(string refundId, DateTimeOffset now)
    {
        var result = TransitionTo(OrderStatus.Refunded, Actor.System, now, "gateway refund succeeded");
        if (result.Transitioned)
        {
            RefundId = refundId;
        }

        return result;
    }

    /// <summary>
    /// Records a failed refund attempt. Schedules the next attempt with the
    /// caller-computed backoff, or — once attempts are exhausted — transitions
    /// to the terminal refund_failed state and raises the manual-intervention
    /// flag the dashboard surfaces.
    /// </summary>
    public TransitionResult RecordRefundFailure(string error, DateTimeOffset now, int maxAttempts, TimeSpan nextBackoff)
    {
        if (Status != OrderStatus.RefundPending)
        {
            return TransitionResult.Ignored;
        }

        RefundAttempts++;
        LastRefundError = error;
        UpdatedAt = now;

        if (RefundAttempts >= maxAttempts)
        {
            ManualInterventionRequired = true;
            NextRefundAttemptAt = null;
            return TransitionTo(OrderStatus.RefundFailed, Actor.System, now, $"retries exhausted after {RefundAttempts} attempts: {error}");
        }

        NextRefundAttemptAt = now + nextBackoff;
        _pendingEvents.Add(new RefundAttemptFailed(Snapshot(), RefundAttempts, error, now));
        return TransitionResult.Ignored;
    }

    /// <summary>
    /// Drains pending domain events. Called exclusively by the DbContext when
    /// saving, which turns each event into an outbox row in the same
    /// transaction.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DequeuePendingEvents()
    {
        var events = _pendingEvents.ToArray();
        _pendingEvents.Clear();
        return events;
    }

    private OrderSnapshot Snapshot() => new(
        Id,
        RestaurantId,
        CustomerId,
        Status,
        Total,
        _lines.AsReadOnly(),
        CreatedAt,
        ExpiresAt,
        RefundAttempts,
        LastRefundError,
        ManualInterventionRequired);
}
