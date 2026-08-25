using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application;
using Ordering.Application.Abstractions;
using Ordering.Domain;

namespace Ordering.Infrastructure.Payments;

/// <summary>
/// Deterministic outbound refunds: fail N transfers then succeed. Destination
/// is the recorded payer wallet; the tx hash is a pure function of destination
/// + amount + attempt so a replayed transfer is a new hash only when the
/// attempt number changes. Not a card refund of a charge id.
/// </summary>
public sealed class FakeRefundRail : IRefundRail
{
    public IReadOnlyList<(string Destination, long AmountMinorUnits, string? TxHash)> Transfers => [.. _transfers];

    private readonly ConcurrentQueue<(string Destination, long AmountMinorUnits, string? TxHash)> _transfers = [];
    private readonly ILogger<FakeRefundRail> _logger;
    private int _failNext;
    private int _attempt;

    public FakeRefundRail(IOptions<OrderingOptions> options, ILogger<FakeRefundRail> logger)
    {
        _logger = logger;
        _failNext = options.Value.Gateway.FailNextRefunds;
    }

    public void InjectFailures(int count) => Interlocked.Add(ref _failNext, count);

    public Task<RefundResult> TransferAsync(string destination, Money amount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var attempt = Interlocked.Increment(ref _attempt);

        if (TryConsume(ref _failNext))
        {
            _transfers.Enqueue((destination, amount.MinorUnits, null));
            _logger.LogWarning("Simulated refund rail failed transfer to {Destination}", destination);
            return Task.FromResult(RefundResult.Fail("Simulated refund rail failure."));
        }

        var txHash = "0x" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"refund:{destination}:{amount.MinorUnits}:{attempt}")))
            .ToLowerInvariant();
        _transfers.Enqueue((destination, amount.MinorUnits, txHash));
        return Task.FromResult(RefundResult.Ok(txHash));
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
