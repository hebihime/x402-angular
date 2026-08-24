using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Payments;
using Ordering.Infrastructure.Projections;
using Ordering.Infrastructure.Workers;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Invariant 4: payment at confirm only; refunds are their own gateway
/// lifecycle owned by the refund worker — retry with exponential backoff, and
/// a terminal refund_failed + manual-intervention flag when retries exhaust.
/// Test config: MaxAttempts=3, backoff base 2000ms.
/// </summary>
[Collection("integration")]
public class RefundLifecycleTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    private SimulatedPaymentGateway Gateway => factory.Services.GetRequiredService<SimulatedPaymentGateway>();
    private RefundProcessor Refunds => factory.Services.GetRequiredService<RefundProcessor>();

    /// <summary>Settle refund_pending leftovers from other tests so injected failure counters hit only this test's order.</summary>
    private async Task DrainOtherRefundsAsync()
    {
        while (await Refunds.RunOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

    private async Task<Guid> PaidOrderAsync(string customer, string key)
    {
        var placed = await _api.PlacedAsync(customer, key);
        var orderId = placed.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(orderId, customer)).EnsureSuccessStatusCode();
        return orderId;
    }

    [Fact]
    public async Task A_declined_charge_leaves_the_draft_unpaid_and_is_retryable()
    {
        var placed = await _api.PlacedAsync("refund-cust-1", "refund-key-1");
        var orderId = placed.GetProperty("orderId").GetGuid();

        Gateway.InjectChargeFailures(1);
        var declined = await _api.ConfirmAsync(orderId, "refund-cust-1");
        declined.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await _api.WriteStatusAsync(orderId)).Should().Be("draft", "a failed charge transitions nothing");

        (await ApiDriver.ReadJsonAsync(await _api.ConfirmAsync(orderId, "refund-cust-1")))
            .GetProperty("status").GetString().Should().Be("paid");
    }

    [Fact]
    public async Task The_refund_worker_retries_with_backoff_and_recovers()
    {
        await DrainOtherRefundsAsync();
        var orderId = await PaidOrderAsync("refund-cust-2", "refund-key-2");
        Gateway.InjectRefundFailures(2);
        await _api.DashboardAsync(orderId, "reject", new { reason = "demo failure run" });

        // Attempt 1 fails; the next attempt is scheduled base*2^0 = 2s out.
        (await Refunds.RunOnceAsync(CancellationToken.None)).Should().BeGreaterThan(0);
        (await _api.WriteStatusAsync(orderId)).Should().Be("refund_pending");
        (await _api.ScalarAsync<int>("SELECT refund_attempts FROM orders WHERE id = @orderId", new { orderId })).Should().Be(1);

        // Not due yet: the worker respects the schedule.
        (await Refunds.RunOnceAsync(CancellationToken.None)).Should().Be(0);

        factory.Clock.Advance(TimeSpan.FromMilliseconds(2000));
        (await Refunds.RunOnceAsync(CancellationToken.None)).Should().BeGreaterThan(0); // attempt 2 fails; next in 4s
        (await _api.ScalarAsync<int>("SELECT refund_attempts FROM orders WHERE id = @orderId", new { orderId })).Should().Be(2);

        factory.Clock.Advance(TimeSpan.FromMilliseconds(4000));
        (await Refunds.RunOnceAsync(CancellationToken.None)).Should().BeGreaterThan(0); // attempt 3 succeeds

        (await _api.WriteStatusAsync(orderId)).Should().Be("refunded");
        (await _api.ScalarAsync<string?>("SELECT refund_id FROM orders WHERE id = @orderId", new { orderId }))
            .Should().StartWith("re_");
        (await _api.ScalarAsync<int>("SELECT refund_attempts FROM orders WHERE id = @orderId", new { orderId }))
            .Should().Be(2, "the successful attempt is not a failure");
    }

    [Fact]
    public async Task Exhausted_retries_end_terminal_refund_failed_with_the_manual_flag_visible_to_the_dashboard()
    {
        await DrainOtherRefundsAsync();
        var orderId = await PaidOrderAsync("refund-cust-3", "refund-key-3");
        Gateway.InjectRefundFailures(3); // == MaxAttempts
        await _api.DashboardAsync(orderId, "reject", new { reason = "exhaustion run" });

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            (await Refunds.RunOnceAsync(CancellationToken.None)).Should().BeGreaterThan(0);
            factory.Clock.Advance(TimeSpan.FromMilliseconds(60000));
        }

        (await _api.WriteStatusAsync(orderId)).Should().Be("refund_failed");

        // Terminal: nothing further is scheduled or processed.
        (await Refunds.RunOnceAsync(CancellationToken.None)).Should().Be(0);

        var processor = factory.Services.GetRequiredService<OutboxProjectionProcessor>();
        while (await processor.RunOnceAsync(CancellationToken.None) > 0)
        {
        }

        var details = await ApiDriver.ReadJsonAsync(await _api.Client.GetAsync($"/api/orders/{orderId}"));
        details.GetProperty("status").GetString().Should().Be("refund_failed");
        details.GetProperty("manualInterventionRequired").GetBoolean().Should().BeTrue();
        details.GetProperty("refundAttempts").GetInt32().Should().Be(3);
        details.GetProperty("lastRefundError").GetString().Should().Contain("Simulated");

        var tail = await _api.ScalarAsync<string>(
            "SELECT string_agg(\"to\", '>' ORDER BY id) FROM status_history WHERE order_id = @orderId",
            new { orderId });
        tail.Should().Be("draft>paid>rejected>refund_pending>refund_failed");
    }
}
