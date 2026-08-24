using MediatR;

namespace Ordering.Application.Abstractions;

/// <summary>Marker for all commands; the TransactionBehavior only wraps these.</summary>
public interface ICommandBase;

public interface ICommand<out TResponse> : IRequest<TResponse>, ICommandBase;

/// <summary>
/// Queries never open a write transaction and their handlers read only the
/// Dapper projection tables, never the DbContext.
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;

/// <summary>
/// A command that must be safe to retry with the same idempotency key. The
/// real guarantee is a database unique constraint; the IdempotencyBehavior
/// turns a retried command into the original response.
/// </summary>
public interface IIdempotentCommand<out TResponse> : ICommand<TResponse>
{
    string CustomerId { get; }
    string IdempotencyKey { get; }
}

/// <summary>
/// Loads the response an earlier execution of an idempotent command already
/// produced. Implementations exist only for commands implementing
/// <see cref="IIdempotentCommand{TResponse}"/>; the behavior resolves them by
/// the command's concrete type.
/// </summary>
public interface IIdempotencyReplayer<in TCommand, TResponse>
    where TCommand : notnull
{
    Task<TResponse?> FindExistingAsync(TCommand command, CancellationToken cancellationToken);
}
