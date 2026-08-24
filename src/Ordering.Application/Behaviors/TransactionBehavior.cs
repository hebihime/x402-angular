using MediatR;
using Ordering.Application.Abstractions;

namespace Ordering.Application.Behaviors;

/// <summary>
/// Pipeline step 4: opens the database transaction around command handlers,
/// making "status update + history row + outbox row commit or roll back
/// together" structural rather than disciplined. Queries never pass through
/// here (they are not <see cref="ICommandBase"/>) and so never open a write
/// transaction.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(ITransactionManager transactionManager)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICommandBase)
        {
            return await next();
        }

        return await transactionManager.InTransactionAsync(() => next(), cancellationToken);
    }
}
