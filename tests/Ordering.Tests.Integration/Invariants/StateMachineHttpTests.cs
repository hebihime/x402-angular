using System.Net;
using FluentAssertions;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Invariant 3 at the HTTP surface: invalid or repeated transitions return the
/// current state with no side effects, and the actor comes from the surface,
/// never the payload.
/// </summary>
[Collection("integration")]
public class StateMachineHttpTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    [Fact]
    public async Task Out_of_order_dashboard_transitions_are_ignored_without_side_effects()
    {
        var placed = await _api.PlacedAsync("sm-cust-1", "sm-key-1");
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "sm-cust-1");
        var historyBefore = await _api.HistoryCountAsync(orderId);
        var outboxBefore = await _api.OutboxCountAsync(orderId);

        // paid -> preparing/ready/completed all skip states; each returns 'paid' untouched.
        foreach (var action in new[] { "start-preparing", "mark-ready", "complete" })
        {
            var result = await ApiDriver.ReadJsonAsync(await _api.DashboardAsync(orderId, action));
            result.GetProperty("status").GetString().Should().Be("paid", "{0} is not legal from paid", action);
        }

        (await _api.HistoryCountAsync(orderId)).Should().Be(historyBefore);
        (await _api.OutboxCountAsync(orderId)).Should().Be(outboxBefore);
    }

    [Fact]
    public async Task A_customer_cannot_cancel_after_payment()
    {
        var placed = await _api.PlacedAsync("sm-cust-2", "sm-key-2");
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "sm-cust-2");

        var cancel = await ApiDriver.ReadJsonAsync(await _api.CancelAsync(orderId, "sm-cust-2"));
        cancel.GetProperty("status").GetString().Should().Be("paid", "draft->cancelled does not apply to a paid order");
        (await _api.WriteStatusAsync(orderId)).Should().Be("paid");
    }

    [Fact]
    public async Task The_full_kitchen_happy_path_walks_the_table_in_order()
    {
        var placed = await _api.PlacedAsync("sm-cust-3", "sm-key-3");
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "sm-cust-3");

        foreach (var (action, expected) in new[]
        {
            ("accept", "accepted"),
            ("start-preparing", "preparing"),
            ("mark-ready", "ready"),
            ("complete", "completed"),
        })
        {
            var result = await ApiDriver.ReadJsonAsync(await _api.DashboardAsync(orderId, action));
            result.GetProperty("status").GetString().Should().Be(expected);
        }

        // (none)->draft->paid->accepted->preparing->ready->completed = 6 rows.
        (await _api.HistoryCountAsync(orderId)).Should().Be(6);
    }

    [Fact]
    public async Task Rejection_starts_the_refund_lifecycle_in_the_same_transaction()
    {
        var placed = await _api.PlacedAsync("sm-cust-4", "sm-key-4");
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "sm-cust-4");

        var rejected = await ApiDriver.ReadJsonAsync(await _api.DashboardAsync(orderId, "reject", new { reason = "no stock" }));
        rejected.GetProperty("status").GetString().Should().Be("refund_pending");

        var tail = await _api.ScalarAsync<string>(
            "SELECT string_agg(\"to\", '>' ORDER BY id) FROM status_history WHERE order_id = @orderId",
            new { orderId });
        tail.Should().Be("draft>paid>rejected>refund_pending");
    }

    [Fact]
    public async Task Transitions_for_a_different_restaurants_order_are_not_found()
    {
        var placed = await _api.PlacedAsync("sm-cust-5", "sm-key-5");
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "sm-cust-5");

        var response = await _api.Client.PostAsync(
            $"/api/restaurants/{Ordering.Infrastructure.Persistence.SeedData.NoodleNexusId}/orders/{orderId}/accept",
            System.Net.Http.Json.JsonContent.Create(new { }));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.WriteStatusAsync(orderId)).Should().Be("paid");
    }

    [Fact]
    public async Task A_customer_cannot_act_on_someone_elses_order()
    {
        var placed = await _api.PlacedAsync("sm-cust-6", "sm-key-6");
        var orderId = placed.GetProperty("orderId").GetGuid();

        (await _api.ConfirmAsync(orderId, "sm-intruder")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.CancelAsync(orderId, "sm-intruder")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.WriteStatusAsync(orderId)).Should().Be("draft");
    }
}
