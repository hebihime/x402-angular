using System.Net;
using FluentAssertions;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>Invariant 5: idempotency on every mutating surface, backed by DB constraints.</summary>
[Collection("integration")]
public class IdempotencyTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    [Fact]
    public async Task Replayed_place_returns_the_existing_draft_even_with_a_different_body()
    {
        var first = await _api.PlacedAsync("idem-cust-1", "idem-key-1", quantity: 2);
        var replay = await _api.PlaceDiavolaAsync("idem-cust-1", "idem-key-1", quantity: 5);

        replay.EnsureSuccessStatusCode();
        var replayed = await ApiDriver.ReadJsonAsync(replay);
        replayed.GetProperty("orderId").GetGuid().Should().Be(first.GetProperty("orderId").GetGuid());
        replayed.GetProperty("total").GetString().Should().Be("2900", "the original draft wins, not the retried body");

        var rows = await _api.ScalarAsync<int>("SELECT COUNT(*) FROM orders WHERE customer_id = 'idem-cust-1'");
        rows.Should().Be(1);
    }

    [Fact]
    public async Task The_same_key_from_a_different_customer_creates_a_distinct_order()
    {
        var a = await _api.PlacedAsync("idem-cust-2a", "idem-shared-key");
        var b = await _api.PlacedAsync("idem-cust-2b", "idem-shared-key");
        a.GetProperty("orderId").GetGuid().Should().NotBe(b.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task Concurrent_places_with_one_key_converge_on_one_order()
    {
        var responses = await Task.WhenAll(
            _api.PlaceDiavolaAsync("idem-cust-3", "idem-race-key", 1),
            _api.PlaceDiavolaAsync("idem-cust-3", "idem-race-key", 1),
            _api.PlaceDiavolaAsync("idem-cust-3", "idem-race-key", 1));

        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
            ids.Add((await ApiDriver.ReadJsonAsync(response)).GetProperty("orderId").GetGuid());
        }

        ids.Distinct().Should().HaveCount(1, "the unique constraint makes the race converge");
        (await _api.ScalarAsync<int>("SELECT COUNT(*) FROM orders WHERE customer_id = 'idem-cust-3'")).Should().Be(1);
    }

    [Fact]
    public async Task Replayed_confirm_settles_nothing_and_returns_the_original_success()
    {
        var placed = await _api.PlacedAsync("idem-cust-4", "idem-key-4");
        var orderId = placed.GetProperty("orderId").GetGuid();

        var first = await ApiDriver.ReadJsonAsync(await _api.ConfirmAsync(orderId, "idem-cust-4"));
        var replay = await ApiDriver.ReadJsonAsync(await _api.ConfirmAsync(orderId, "idem-cust-4"));

        first.GetProperty("status").GetString().Should().Be("paid");
        replay.GetProperty("status").GetString().Should().Be("paid");

        var chargeIds = await _api.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT charge_id) FROM orders WHERE id = @orderId AND charge_id IS NOT NULL",
            new { orderId });
        chargeIds.Should().Be(1);

        var paidTransitions = await _api.ScalarAsync<int>(
            "SELECT COUNT(*) FROM status_history WHERE order_id = @orderId AND \"to\" = 'paid'",
            new { orderId });
        paidTransitions.Should().Be(1, "a replayed confirm must not settle or transition again");
    }

    [Fact]
    public async Task Replayed_dashboard_transition_is_a_no_op_returning_current_state()
    {
        var placed = await _api.PlacedAsync("idem-cust-5", "idem-key-5");
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "idem-cust-5");

        (await ApiDriver.ReadJsonAsync(await _api.DashboardAsync(orderId, "accept")))
            .GetProperty("status").GetString().Should().Be("accepted");
        var replay = await ApiDriver.ReadJsonAsync(await _api.DashboardAsync(orderId, "accept"));
        replay.GetProperty("status").GetString().Should().Be("accepted");

        var acceptedRows = await _api.ScalarAsync<int>(
            "SELECT COUNT(*) FROM status_history WHERE order_id = @orderId AND \"to\" = 'accepted'",
            new { orderId });
        acceptedRows.Should().Be(1);
    }
}
