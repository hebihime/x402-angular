using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// The transaction the TransactionBehavior opens around every command handler.
/// Nested commands (none exist today) would join the ambient transaction
/// rather than open a second one.
/// </summary>
public sealed class EfTransactionManager(OrderingDbContext dbContext) : ITransactionManager
{
    public async Task<TResponse> InTransactionAsync<TResponse>(Func<Task<TResponse>> action, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await action();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await action();
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
