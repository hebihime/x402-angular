namespace Ordering.Infrastructure.Outbox;

/// <summary>
/// The outbox table IS the event system: every domain event becomes one row,
/// inserted in the same transaction as the state change that raised it. The
/// projector drains rows in id order and flips <see cref="ProcessedAt"/>.
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }
    public Guid OrderId { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
