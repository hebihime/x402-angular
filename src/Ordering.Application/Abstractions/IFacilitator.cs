namespace Ordering.Application.Abstractions;

public abstract record FacilitatorVerifyResult
{
    public sealed record Valid(string PayerAddress) : FacilitatorVerifyResult;

    public sealed record Invalid(string Reason) : FacilitatorVerifyResult;
}

public abstract record FacilitatorSettleResult
{
    public sealed record Succeeded(string PayerAddress, string TxHash) : FacilitatorSettleResult;

    public sealed record Failed(string Reason) : FacilitatorSettleResult;
}

/// <summary>
/// x402 facilitator: verify is the free off-chain check that reveals the
/// payer; settle moves funds. Callers must not reach this seam for an order
/// that already has a payment row — replays are answered earlier.
/// </summary>
public interface IFacilitator
{
    Task<FacilitatorVerifyResult> VerifyAsync(
        string paymentHeader,
        ExactPaymentRequirements requirements,
        CancellationToken cancellationToken);

    Task<FacilitatorSettleResult> SettleAsync(
        string paymentHeader,
        ExactPaymentRequirements requirements,
        CancellationToken cancellationToken);
}
