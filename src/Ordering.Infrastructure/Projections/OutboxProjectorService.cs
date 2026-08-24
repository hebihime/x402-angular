using Microsoft.Extensions.Logging;
using Ordering.Infrastructure.Workers;

namespace Ordering.Infrastructure.Projections;

public sealed class OutboxProjectorService(
    OutboxProjectionProcessor processor,
    TimeProvider clock,
    ILogger<OutboxProjectorService> logger) : PollingBackgroundService(clock, logger)
{
    protected override string WorkerName => "Outbox projector";
    protected override TimeSpan IdleDelay => TimeSpan.FromMilliseconds(200);

    protected override Task<int> RunOnceAsync(CancellationToken cancellationToken) =>
        processor.RunOnceAsync(cancellationToken);
}
