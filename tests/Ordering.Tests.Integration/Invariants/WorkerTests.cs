using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Workers;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Phase 6: the draft-expiry and acceptance-timeout workers, driven
/// deterministically with the fake clock. Test config: draft TTL 200000s,
/// acceptance timeout 900s.
/// </summary>
[Collection("integration")]
public class WorkerTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    [Fact]
    public async Task The_expiry_worker_expires_drafts_whose_ttl_elapsed()
    {
        var placed = await _api.PlacedAsync("worker-cust-1", "worker-key-1");
        var orderId = placed.GetProperty("orderId").GetGuid();

        var expiry = factory.Services.GetRequiredService<ExpiryProcessor>();
        await expiry.RunOnceAsync(CancellationToken.None);
        (await _api.WriteStatusAsync(orderId)).Should().Be("draft", "the TTL has not elapsed yet");

        factory.Clock.Advance(TimeSpan.FromSeconds(200001));
        while (await expiry.RunOnceAsync(CancellationToken.None) > 0)
        {
        }

        (await _api.WriteStatusAsync(orderId)).Should().Be("expired");
        var tail = await _api.ScalarAsync<string>(
            "SELECT string_agg(actor, '>' ORDER BY id) FROM status_history WHERE order_id = @orderId",
            new { orderId });
        tail.Should().Be("customer>system", "expiry is a system transition");
    }

    [Fact]
    public async Task Confirming_an_expired_but_unswept_draft_conflicts_and_expires_it()
    {
        var placed = await _api.PlacedAsync("worker-cust-2", "worker-key-2");
        var orderId = placed.GetProperty("orderId").GetGuid();

        factory.Clock.Advance(TimeSpan.FromSeconds(200001));

        var confirm = await _api.ConfirmAsync(orderId, "worker-cust-2");
        confirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _api.WriteStatusAsync(orderId)).Should().Be("expired", "the TTL is a domain rule, not a worker detail");
    }

    [Fact]
    public async Task The_acceptance_timeout_worker_rejects_ignored_paid_orders_into_the_refund_lifecycle()
    {
        var placed = await _api.PlacedAsync("worker-cust-3", "worker-key-3");
        var orderId = placed.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(orderId, "worker-cust-3")).EnsureSuccessStatusCode();

        var timeout = factory.Services.GetRequiredService<AcceptanceTimeoutProcessor>();
        await timeout.RunOnceAsync(CancellationToken.None);
        (await _api.WriteStatusAsync(orderId)).Should().Be("paid", "the restaurant still has time");

        factory.Clock.Advance(TimeSpan.FromSeconds(901));
        while (await timeout.RunOnceAsync(CancellationToken.None) > 0)
        {
        }

        (await _api.WriteStatusAsync(orderId)).Should().Be("refund_pending");
        var actors = await _api.ScalarAsync<string>(
            "SELECT string_agg(\"to\" || ':' || actor, '>' ORDER BY id) FROM status_history WHERE order_id = @orderId",
            new { orderId });
        actors.Should().Be("draft:customer>paid:system>rejected:system>refund_pending:system");
    }
}
