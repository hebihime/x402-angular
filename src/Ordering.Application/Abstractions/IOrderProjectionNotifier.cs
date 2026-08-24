namespace Ordering.Application.Abstractions;

/// <summary>
/// What the projector broadcasts after committing a projection update. The
/// dashboard patches its board in place from these and refetches on reconnect.
/// </summary>
public sealed record OrderProjectionEvent(string EventType, OrderSummaryDto Order, HistoryEntryDto? HistoryDelta);

public interface IOrderProjectionNotifier
{
    Task PublishAsync(OrderProjectionEvent projectionEvent, CancellationToken cancellationToken);
}
