using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Payments;
using Ordering.Infrastructure.Projections;
using Ordering.Infrastructure.Workers;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Invariant 4: payment at confirm only (x402); refunds are their own
/// lifecycle owned by the refund worker — retry with exponential backoff, and
/// a terminal refund_failed + manual-intervention flag when retries exhaust.
/// Test config: MaxAttempts=3, backoff base 2000ms.
/// </summary>
[Collection("integration")]
public class RefundLifecycleTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    private FakeRefundRail Rail => factory.Services.GetRequiredService<FakeRefundRail>();
    private FakeFacilitator Facilitator => factory.Services.GetRequiredService<FakeFacilitator>();
    private RefundProcessor Refunds => factory.Services.GetRequiredService<RefundProcessor>();

    /// <summary>Settle refund_pending leftovers from other tests so injected failure counters hit only this test's order.</summary>
    private async Task DrainOtherRefundsAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            if (await Refunds.RunOnceAsync(CancellationToken.None) == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "Refund drain did not idle after 100 passes; a due refund_pending row may be stuck.");
    }

    private async Task<Guid> PaidOrderAsync(string customer, string key)
    {
        var placed = await _api.PlacedAsync(customer, key);
        var orderId = placed.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(orderId, customer)).EnsureSuccessStatusCode();
        return orderId;
    }

    [Fact]
    public async Task A_declined_settlement_leaves_the_draft_unpaid_and_is_retryable()
    {
        var placed = await _api.PlacedAsync("refund-cust-1", "refund-key-1");
        var orderId = placed.GetProperty("orderId").GetGuid();

        Facilitator.InjectSettleFailures(1);
        var declined = await _api.ConfirmAsync(orderId, "refund-cust-1");
        declined.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await _api.WriteStatusAsync(orderId)).Should().Be("draft", "a failed settlement transitions nothing");

        (await ApiDriver.ReadJsonAsync(await _api.ConfirmAsync(orderId, "refund-cust-1")))
            .GetProperty("status").GetString().Should().Be("paid");
    }

    [Fact]
    public async Task The_refund_worker_retries_with_backoff_and_recovers()
    {
        await DrainOtherRefundsAsync();
        var orderId = await PaidOrderAsync("refund-cust-2", "refund-key-2");
        Rail.InjectFailures(2);
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
            .Should().StartWith("0x");
        (await _api.ScalarAsync<int>("SELECT refund_attempts FROM orders WHERE id = @orderId", new { orderId }))
            .Should().Be(2, "the successful attempt is not a failure");
    }

    [Fact]
    public async Task Exhausted_retries_end_terminal_refund_failed_with_the_manual_flag_visible_to_the_dashboard()
    {
        await DrainOtherRefundsAsync();
        var orderId = await PaidOrderAsync("refund-cust-3", "refund-key-3");
        Rail.InjectFailures(3); // == MaxAttempts
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
        details.GetProperty("lastRefundError").GetString().Should().Contain("Simulated refund rail");

        var tail = await _api.ScalarAsync<string>(
            "SELECT string_agg(\"to\", '>' ORDER BY id) FROM status_history WHERE order_id = @orderId",
            new { orderId });
        tail.Should().Be("draft>paid>rejected>refund_pending>refund_failed");
    }

    [Fact]
    public async Task Refund_transfer_goes_to_the_recorded_payer_wallet()
    {
        await DrainOtherRefundsAsync();
        var transfersBefore = Rail.Transfers.Count;
        var orderId = await PaidOrderAsync("refund-cust-payer", "refund-key-payer");
        await _api.DashboardAsync(orderId, "reject", new { reason = "payer rail" });
        (await Refunds.RunOnceAsync(CancellationToken.None)).Should().BeGreaterThan(0);

        (await _api.WriteStatusAsync(orderId)).Should().Be("refunded");
        var added = Rail.Transfers.Skip(transfersBefore).ToList();
        added.Should().ContainSingle();
        added[0].Destination.Should().Be(ApiDriver.DefaultPayer);
        added[0].AmountMinorUnits.Should().Be(1450);
        added[0].TxHash.Should().StartWith("0x");
    }

    [Fact]
    public async Task A_refund_pending_order_without_a_payer_is_not_refunded_even_if_charge_id_is_set()
    {
        await DrainOtherRefundsAsync();
        var transfersBefore = Rail.Transfers.Count;
        var orderId = await PaidOrderAsync("refund-cust-nopayer", "refund-key-nopayer");
        await _api.DashboardAsync(orderId, "reject", new { reason = "no payer" });
        (await _api.ScalarAsync<string?>("SELECT charge_id FROM orders WHERE id = @orderId", new { orderId }))
            .Should().NotBeNull("phase 1 still records the settlement tx on the order; the rail must not use it");

        await _api.ExecuteAsync($"UPDATE orders SET payer_address = NULL WHERE id = '{orderId}'");

        (await Refunds.RunOnceAsync(CancellationToken.None)).Should().BeGreaterThan(0);
        (await _api.WriteStatusAsync(orderId)).Should().Be("refund_pending");
        Rail.Transfers.Count.Should().Be(transfersBefore, "no destination means the rail is never called");

        // Pull it out of the due scan so later tests' DrainOtherRefundsAsync
        // cannot hot-loop on a refund_pending row with no destination.
        await _api.ExecuteAsync($"UPDATE orders SET next_refund_attempt_at = NULL WHERE id = '{orderId}'");
    }
}
