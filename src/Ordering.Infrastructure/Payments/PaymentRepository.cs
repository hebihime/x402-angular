using Microsoft.EntityFrameworkCore;
using Ordering.Application.Abstractions;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Infrastructure.Payments;

public sealed class PaymentRepository(OrderingDbContext dbContext) : IPaymentRepository
{
    public async Task<PaymentRecord?> FindByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<bool> TryAddAsync(PaymentRecord payment, CancellationToken cancellationToken)
    {
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO payments (id, order_id, payer_address, amount_minor_units, payment_payload_hash, tx_hash, settled_at)
             VALUES ({payment.Id}, {payment.OrderId}, {payment.PayerAddress}, {payment.AmountMinorUnits}, {payment.PaymentPayloadHash}, {payment.TxHash}, {payment.SettledAt})
             ON CONFLICT DO NOTHING
             """,
            cancellationToken);
        return inserted == 1;
    }

    public Task AcquirePayerAdvisoryLockAsync(string payerAddress, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({payerAddress}))",
            cancellationToken);

    private static PaymentRecord ToRecord(Payment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.PayerAddress,
        payment.AmountMinorUnits,
        payment.PaymentPayloadHash,
        payment.TxHash,
        payment.SettledAt);
}
