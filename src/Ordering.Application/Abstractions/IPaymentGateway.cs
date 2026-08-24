using Ordering.Domain;

namespace Ordering.Application.Abstractions;

public sealed record GatewayResult(bool Succeeded, string? TransactionId, string? Error)
{
    public static GatewayResult Ok(string transactionId) => new(true, transactionId, null);
    public static GatewayResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// The payment provider. Charges happen only at confirm; refunds are a
/// separate gateway operation with their own lifecycle, never a reversal.
/// The order id doubles as the gateway idempotency key, so a replayed charge
/// or refund yields the same transaction id instead of settling twice.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayResult> ChargeAsync(Guid orderId, string customerId, Money amount, CancellationToken cancellationToken);

    Task<GatewayResult> RefundAsync(Guid orderId, string chargeId, Money amount, CancellationToken cancellationToken);
}
