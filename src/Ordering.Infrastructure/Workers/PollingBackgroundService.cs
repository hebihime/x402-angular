using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ordering.Infrastructure.Workers;

/// <summary>
/// Shared polling loop for the projector and the lifecycle workers: run one
/// pass, log failures without dying, poll faster while there is work.
/// </summary>
public abstract class PollingBackgroundService(TimeProvider clock, ILogger logger) : BackgroundService
{
    protected abstract string WorkerName { get; }
    protected abstract TimeSpan IdleDelay { get; }
    protected virtual TimeSpan BusyDelay => TimeSpan.FromMilliseconds(10);

    /// <summary>Returns the number of items processed in this pass.</summary>
    protected abstract Task<int> RunOnceAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Worker} started", WorkerName);
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "{Worker} pass failed; will retry", WorkerName);
            }

            try
            {
                await Task.Delay(processed > 0 ? BusyDelay : IdleDelay, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("{Worker} stopped", WorkerName);
    }
}
