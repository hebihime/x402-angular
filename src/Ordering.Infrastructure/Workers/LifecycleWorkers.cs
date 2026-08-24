using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application;
using Ordering.Application.Orders.Commands;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Infrastructure.Workers;

// Each worker scans the write model for due orders and sends the matching
// system command through MediatR, so worker-driven transitions run the exact
// same pipeline (validation → transaction → three writes) as user-driven ones.
// The processors are separated from the BackgroundService loops so tests can
// drive a single pass deterministically with a fake clock.

public sealed class ExpiryProcessor(IServiceScopeFactory scopeFactory)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var dueOrderIds = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Draft && o.ExpiresAt <= now)
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var orderId in dueOrderIds)
        {
            await sender.Send(new ExpireOrderCommand(orderId), cancellationToken);
        }

        return dueOrderIds.Count;
    }
}

public sealed class AcceptanceTimeoutProcessor(IServiceScopeFactory scopeFactory)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<OrderingOptions>>().Value;
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var cutoff = now - TimeSpan.FromSeconds(options.AcceptanceTimeoutSeconds);

        var dueOrderIds = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid && o.UpdatedAt <= cutoff)
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var orderId in dueOrderIds)
        {
            await sender.Send(new TimeoutOrderAcceptanceCommand(orderId), cancellationToken);
        }

        return dueOrderIds.Count;
    }
}

public sealed class RefundProcessor(IServiceScopeFactory scopeFactory)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var dueOrderIds = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.RefundPending
                && o.NextRefundAttemptAt != null
                && o.NextRefundAttemptAt <= now)
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var orderId in dueOrderIds)
        {
            await sender.Send(new ProcessRefundCommand(orderId), cancellationToken);
        }

        return dueOrderIds.Count;
    }
}

public sealed class ExpiryWorkerService(ExpiryProcessor processor, TimeProvider clock, ILogger<ExpiryWorkerService> logger)
    : PollingBackgroundService(clock, logger)
{
    protected override string WorkerName => "Draft-expiry worker";
    protected override TimeSpan IdleDelay => TimeSpan.FromMilliseconds(500);

    protected override Task<int> RunOnceAsync(CancellationToken cancellationToken) => processor.RunOnceAsync(cancellationToken);
}

public sealed class AcceptanceTimeoutWorkerService(AcceptanceTimeoutProcessor processor, TimeProvider clock, ILogger<AcceptanceTimeoutWorkerService> logger)
    : PollingBackgroundService(clock, logger)
{
    protected override string WorkerName => "Acceptance-timeout worker";
    protected override TimeSpan IdleDelay => TimeSpan.FromMilliseconds(500);

    protected override Task<int> RunOnceAsync(CancellationToken cancellationToken) => processor.RunOnceAsync(cancellationToken);
}

public sealed class RefundWorkerService(RefundProcessor processor, TimeProvider clock, ILogger<RefundWorkerService> logger)
    : PollingBackgroundService(clock, logger)
{
    protected override string WorkerName => "Refund worker";
    protected override TimeSpan IdleDelay => TimeSpan.FromMilliseconds(500);

    protected override Task<int> RunOnceAsync(CancellationToken cancellationToken) => processor.RunOnceAsync(cancellationToken);
}
