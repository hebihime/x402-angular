using Ordering.Domain;

namespace Ordering.Application.Abstractions;

public sealed record RefundResult(bool Succeeded, string? TxHash, string? Error)
{
    public static RefundResult Ok(string txHash) => new(true, txHash, null);

    public static RefundResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// A new outbound transfer to the recorded payer. x402 settlement does not
/// reverse: this is a push to <c>destination</c>, not an undo of a charge id.
/// </summary>
public interface IRefundRail
{
    Task<RefundResult> TransferAsync(string destination, Money amount, CancellationToken cancellationToken);
}
