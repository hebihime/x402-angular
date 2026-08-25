namespace Ordering.Application.Abstractions;

public sealed record PaymentRequirementsExtra(string Name, string Version);

/// <summary>x402 exact-scheme PaymentRequirements — the 402 <c>accepts[]</c> entry.</summary>
public sealed record ExactPaymentRequirements(
    string Scheme,
    string Network,
    string MaxAmountRequired,
    string Resource,
    string Description,
    string MimeType,
    string PayTo,
    int MaxTimeoutSeconds,
    string Asset,
    PaymentRequirementsExtra Extra);

public sealed record X402Challenge(int X402Version, string Error, IReadOnlyList<ExactPaymentRequirements> Accepts)
{
    public const int Version = 1;
}
