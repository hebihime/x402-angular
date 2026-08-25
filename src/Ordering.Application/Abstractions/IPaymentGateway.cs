using Ordering.Domain;

namespace Ordering.Application.Abstractions;

public sealed record GatewayResult(bool Succeeded, string? TransactionId, string? Error)
{
    public static GatewayResult Ok(string transactionId) => new(true, transactionId, null);
    public static GatewayResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Outbound refund rail (phase 4 will retarget this at the recorded payer).
/// Confirm no longer charges through this port — that is <see cref="IFacilitator"/>.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayResult> RefundAsync(Guid orderId, string chargeId, Money amount, CancellationToken cancellationToken);
}
