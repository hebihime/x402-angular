using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application;
using Ordering.Application.Abstractions;
using Ordering.Domain;

namespace Ordering.Infrastructure.Payments;

/// <summary>
/// The one sanctioned fake: a deterministic, failure-injectable gateway.
/// Transaction ids derive from the order id, so a replayed charge or refund
/// returns the same id instead of settling twice. "Fail the next N calls" is
/// seeded from configuration and can be topped up at runtime for demos/tests.
/// </summary>
public sealed class SimulatedPaymentGateway : IPaymentGateway
{
    private readonly ILogger<SimulatedPaymentGateway> _logger;
    private int _failNextCharges;
    private int _failNextRefunds;

    public SimulatedPaymentGateway(IOptions<OrderingOptions> options, ILogger<SimulatedPaymentGateway> logger)
    {
        _logger = logger;
        _failNextCharges = options.Value.Gateway.FailNextCharges;
        _failNextRefunds = options.Value.Gateway.FailNextRefunds;
    }

    public void InjectChargeFailures(int count) => Interlocked.Add(ref _failNextCharges, count);

    public void InjectRefundFailures(int count) => Interlocked.Add(ref _failNextRefunds, count);

    public Task<GatewayResult> ChargeAsync(Guid orderId, string customerId, Money amount, CancellationToken cancellationToken)
    {
        if (TryConsumeFailure(ref _failNextCharges))
        {
            _logger.LogWarning("Simulated gateway declined charge for order {OrderId}", orderId);
            return Task.FromResult(GatewayResult.Fail("Simulated gateway declined the charge."));
        }

        return Task.FromResult(GatewayResult.Ok($"ch_{orderId:N}"));
    }

    public Task<GatewayResult> RefundAsync(Guid orderId, string chargeId, Money amount, CancellationToken cancellationToken)
    {
        if (TryConsumeFailure(ref _failNextRefunds))
        {
            _logger.LogWarning("Simulated gateway failed refund for order {OrderId}", orderId);
            return Task.FromResult(GatewayResult.Fail("Simulated gateway refund failure."));
        }

        return Task.FromResult(GatewayResult.Ok($"re_{orderId:N}"));
    }

    private static bool TryConsumeFailure(ref int counter)
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
