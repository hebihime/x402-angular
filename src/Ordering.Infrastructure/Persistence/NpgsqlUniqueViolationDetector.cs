using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Persistence;

public sealed class NpgsqlUniqueViolationDetector : IUniqueViolationDetector
{
    public bool IsIdempotencyKeyViolation(Exception exception) =>
        exception is DbUpdateException
        {
            InnerException: PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_orders_customer_idempotency_key",
            },
        };
}
