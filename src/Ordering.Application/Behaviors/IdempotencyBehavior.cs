using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;

namespace Ordering.Application.Behaviors;

/// <summary>
/// Pipeline step 3: for commands marked <see cref="IIdempotentCommand{TResponse}"/>,
/// a retry with the same key returns the original response. The fast path is a
/// lookup before the handler runs; the guarantee is the database unique
/// constraint — if a concurrent duplicate slips past the lookup, the
/// constraint fires and this behavior replays the existing response.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IServiceProvider serviceProvider,
    IUniqueViolationDetector uniqueViolationDetector,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand<TResponse>)
        {
            return await next();
        }

        var replayer = serviceProvider.GetRequiredService<IIdempotencyReplayer<TRequest, TResponse>>();

        var existing = await replayer.FindExistingAsync(request, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("{Request} replayed for an existing idempotency key; no new side effects", typeof(TRequest).Name);
            return existing;
        }

        try
        {
            return await next();
        }
        catch (Exception exception) when (uniqueViolationDetector.IsIdempotencyKeyViolation(exception))
        {
            logger.LogInformation("{Request} lost an idempotency race; returning the winner's response", typeof(TRequest).Name);
            var winner = await replayer.FindExistingAsync(request, cancellationToken);
            return winner ?? throw new InvalidOperationException(
                "Idempotency-key unique constraint fired but the existing record could not be loaded.");
        }
    }
}
