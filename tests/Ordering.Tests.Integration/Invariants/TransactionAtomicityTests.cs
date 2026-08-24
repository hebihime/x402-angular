using System.Net;
using FluentAssertions;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Invariant 2: every state transition is one transaction with three writes
/// (status, history row, outbox row). These tests break one of the three
/// writes at the database level and assert the other two roll back with it.
/// </summary>
[Collection("integration")]
public class TransactionAtomicityTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    [Fact]
    public async Task A_transition_whose_outbox_write_fails_persists_nothing()
    {
        var placed = await _api.PlacedAsync("atomicity-cust-1", "atomicity-key-1");
        var orderId = placed.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(orderId, "atomicity-cust-1")).EnsureSuccessStatusCode();

        var historyBefore = await _api.HistoryCountAsync(orderId);
        var outboxBefore = await _api.OutboxCountAsync(orderId);

        await _api.ExecuteAsync("ALTER TABLE outbox RENAME TO outbox_broken");
        try
        {
            var response = await _api.DashboardAsync(orderId, "accept");
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await _api.ExecuteAsync("ALTER TABLE outbox_broken RENAME TO outbox");
        }

        (await _api.WriteStatusAsync(orderId)).Should().Be("paid", "the status update must roll back with the failed outbox write");
        (await _api.HistoryCountAsync(orderId)).Should().Be(historyBefore, "the history row must roll back too");
        (await _api.OutboxCountAsync(orderId)).Should().Be(outboxBefore);

        // The order is undamaged: the same transition succeeds afterwards.
        var retry = await ApiDriver.ReadJsonAsync(await _api.DashboardAsync(orderId, "accept"));
        retry.GetProperty("status").GetString().Should().Be("accepted");
    }

    [Fact]
    public async Task A_transition_whose_history_write_fails_persists_nothing()
    {
        var placed = await _api.PlacedAsync("atomicity-cust-2", "atomicity-key-2");
        var orderId = placed.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(orderId, "atomicity-cust-2")).EnsureSuccessStatusCode();
        var outboxBefore = await _api.OutboxCountAsync(orderId);

        await _api.ExecuteAsync("ALTER TABLE status_history RENAME TO status_history_broken");
        try
        {
            var response = await _api.DashboardAsync(orderId, "accept");
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await _api.ExecuteAsync("ALTER TABLE status_history_broken RENAME TO status_history");
        }

        (await _api.WriteStatusAsync(orderId)).Should().Be("paid");
        (await _api.OutboxCountAsync(orderId)).Should().Be(outboxBefore, "no event may be emitted without its transition");
    }

    [Fact]
    public async Task A_failed_place_persists_no_order_at_all()
    {
        await _api.ExecuteAsync("ALTER TABLE outbox RENAME TO outbox_broken");
        try
        {
            var response = await _api.PlaceDiavolaAsync("atomicity-cust-3", "atomicity-key-3", 1);
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await _api.ExecuteAsync("ALTER TABLE outbox_broken RENAME TO outbox");
        }

        var count = await _api.ScalarAsync<int>(
            "SELECT COUNT(*) FROM orders WHERE customer_id = 'atomicity-cust-3'");
        count.Should().Be(0, "draft creation is atomic with its history and outbox writes");
    }
}
