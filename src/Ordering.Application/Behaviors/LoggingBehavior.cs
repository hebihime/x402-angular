using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Ordering.Application.Behaviors;

/// <summary>Pipeline step 1: request name, duration, outcome. No domain logic.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            logger.LogInformation("{Request} handled in {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Request} failed after {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
