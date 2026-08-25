using Ordering.Domain;

namespace Ordering.Application.Abstractions;

public sealed record PaymentRecord(
    Guid Id,
    Guid OrderId,
    string PayerAddress,
    long AmountMinorUnits,
    string PaymentPayloadHash,
    string TxHash,
    DateTimeOffset SettledAt);

public interface IPaymentRepository
{
    Task<PaymentRecord?> FindByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the payment. Returns false when a unique constraint lost the
    /// race (ON CONFLICT DO NOTHING) — caller loads the winner's row.
    /// </summary>
    Task<bool> TryAddAsync(PaymentRecord payment, CancellationToken cancellationToken);

    Task AcquirePayerAdvisoryLockAsync(string payerAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Cumulative settled spend for the payer since <paramref name="since"/>,
    /// excluding refunded orders (money that came back).
    /// </summary>
    Task<Money> GetSpendSinceAsync(string payerAddress, DateTimeOffset since, Guid? excludeOrderId, CancellationToken cancellationToken);
}
