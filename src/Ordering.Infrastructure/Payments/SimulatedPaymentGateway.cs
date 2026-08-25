using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application;
using Ordering.Application.Abstractions;
using Ordering.Domain;

namespace Ordering.Infrastructure.Payments;

/// <summary>
/// Simulated outbound refunds until phase 4 retargets them at the recorded
/// payer. Confirm no longer charges here. "Fail the next N refunds" is seeded
/// from configuration and can be topped up at runtime for demos/tests.
/// </summary>
public sealed class SimulatedPaymentGateway : IPaymentGateway
{
    private readonly ILogger<SimulatedPaymentGateway> _logger;
    private int _failNextRefunds;

    public SimulatedPaymentGateway(IOptions<OrderingOptions> options, ILogger<SimulatedPaymentGateway> logger)
    {
        _logger = logger;
        _failNextRefunds = options.Value.Gateway.FailNextRefunds;
    }

    public void InjectRefundFailures(int count) => Interlocked.Add(ref _failNextRefunds, count);

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
