using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Persistence;

public sealed class NpgsqlUniqueViolationDetector : IUniqueViolationDetector
{
    public bool IsIdempotencyKeyViolation(Exception exception) =>
        IsConstraint(exception, "ux_orders_customer_idempotency_key");

    public bool IsSettlementReplayViolation(Exception exception) =>
        IsConstraint(exception, "ux_payments_order_id")
        || IsConstraint(exception, "ux_payments_payload_hash")
        || IsConstraint(exception, "ux_payments_tx_hash")
        || IsConstraint(exception, "ux_orders_charge_id");

    private static bool IsConstraint(Exception exception, string constraintName) =>
        exception is DbUpdateException
        {
            InnerException: PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: var name,
            },
        } && name == constraintName;
}
