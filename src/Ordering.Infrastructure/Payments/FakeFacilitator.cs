using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ordering.Application;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Payments;

/// <summary>
/// Deterministic facilitator: fail N verifies/settles then succeed. Accepts
/// headers from <see cref="EncodePaymentHeader"/>. Settlement tx hash is a
/// pure function of the header so a replayed payload collides on
/// payments.tx_hash the same way a real double-spend would.
/// </summary>
public sealed class FakeFacilitator : IFacilitator
{
    public IReadOnlyList<(string Header, ExactPaymentRequirements Requirements)> Verifies => _verifies;
    public IReadOnlyList<(string Header, ExactPaymentRequirements Requirements)> Settles => _settles;

    private readonly List<(string Header, ExactPaymentRequirements Requirements)> _verifies = [];
    private readonly List<(string Header, ExactPaymentRequirements Requirements)> _settles = [];
    private int _failNextVerifies;
    private int _failNextSettles;

    public FakeFacilitator(IOptions<OrderingOptions> options)
    {
        _failNextVerifies = options.Value.X402.FailNextVerifies;
        _failNextSettles = options.Value.X402.FailNextSettles;
    }

    public void InjectVerifyFailures(int count) => Interlocked.Add(ref _failNextVerifies, count);

    public void InjectSettleFailures(int count) => Interlocked.Add(ref _failNextSettles, count);

    public static string EncodePaymentHeader(string payer, string nonce) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { payer, nonce })));

    public Task<FacilitatorVerifyResult> VerifyAsync(
        string paymentHeader,
        ExactPaymentRequirements requirements,
        CancellationToken cancellationToken)
    {
        _verifies.Add((paymentHeader, requirements));
        if (TryConsume(ref _failNextVerifies))
        {
            return Task.FromResult<FacilitatorVerifyResult>(new FacilitatorVerifyResult.Invalid("Simulated facilitator declined the verification."));
        }

        var payer = DecodePayer(paymentHeader);
        return Task.FromResult<FacilitatorVerifyResult>(
            payer is null
                ? new FacilitatorVerifyResult.Invalid("malformed_payment_header")
                : new FacilitatorVerifyResult.Valid(payer));
    }

    public Task<FacilitatorSettleResult> SettleAsync(
        string paymentHeader,
        ExactPaymentRequirements requirements,
        CancellationToken cancellationToken)
    {
        _settles.Add((paymentHeader, requirements));
        if (TryConsume(ref _failNextSettles))
        {
            return Task.FromResult<FacilitatorSettleResult>(new FacilitatorSettleResult.Failed("Simulated facilitator declined the settlement."));
        }

        var payer = DecodePayer(paymentHeader);
        if (payer is null)
        {
            return Task.FromResult<FacilitatorSettleResult>(new FacilitatorSettleResult.Failed("malformed_payment_header"));
        }

        var txHash = "0x" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("settle:" + paymentHeader))).ToLowerInvariant();
        return Task.FromResult<FacilitatorSettleResult>(new FacilitatorSettleResult.Succeeded(payer, txHash));
    }

    private static string? DecodePayer(string paymentHeader)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(paymentHeader));
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("payer", out var payer) ? payer.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryConsume(ref int counter)
    {
        while (true)
        {
            var current = Volatile.Read(ref counter);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref counter, current - 1, current) == current)
            {
                return true;
            }
        }
    }
}
