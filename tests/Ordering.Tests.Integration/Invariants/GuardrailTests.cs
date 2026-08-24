using System.Net;
using FluentAssertions;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Invariant 6: guardrails key on the customer id — global max order value and
/// a daily cumulative spend cap, enforced at draft creation and re-checked at
/// confirm. Test config: max order 20000, daily cap 50000; Diavola is 1450.
/// </summary>
[Collection("integration")]
public class GuardrailTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    [Fact]
    public async Task Orders_above_the_max_order_value_are_rejected_at_placement()
    {
        // 14 x 1450 = 20300 > 20000.
        var response = await _api.PlaceDiavolaAsync("guard-cust-1", "guard-key-1", 14);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await _api.ScalarAsync<int>("SELECT COUNT(*) FROM orders WHERE customer_id = 'guard-cust-1'"))
            .Should().Be(0, "a guardrail violation must not create a draft");

        // 13 x 1450 = 18850 <= 20000 passes.
        (await _api.PlaceDiavolaAsync("guard-cust-1", "guard-key-1b", 13)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_daily_spend_cap_accumulates_across_orders_per_customer()
    {
        (await _api.PlaceDiavolaAsync("guard-cust-2", "guard-key-2a", 13)).EnsureSuccessStatusCode(); // 18850
        (await _api.PlaceDiavolaAsync("guard-cust-2", "guard-key-2b", 13)).EnsureSuccessStatusCode(); // 37700

        // 37700 + 18850 = 56550 > 50000.
        var third = await _api.PlaceDiavolaAsync("guard-cust-2", "guard-key-2c", 13);
        third.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Another customer is unaffected by this customer's spend.
        (await _api.PlaceDiavolaAsync("guard-cust-2-other", "guard-key-2d", 13)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cancelled_orders_free_their_slice_of_the_daily_cap()
    {
        var a = await _api.PlacedAsync("guard-cust-3", "guard-key-3a", 13); // 18850
        (await _api.PlaceDiavolaAsync("guard-cust-3", "guard-key-3b", 13)).EnsureSuccessStatusCode(); // 37700
        (await _api.PlaceDiavolaAsync("guard-cust-3", "guard-key-3c", 13)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);

        (await _api.CancelAsync(a.GetProperty("orderId").GetGuid(), "guard-cust-3")).EnsureSuccessStatusCode();

        (await _api.PlaceDiavolaAsync("guard-cust-3", "guard-key-3d", 13)).StatusCode
            .Should().Be(HttpStatusCode.Created, "a cancelled draft no longer counts toward the cap");
    }

    [Fact]
    public async Task Confirm_rechecks_the_daily_cap_against_spend_since_placement()
    {
        // Yesterday: a draft placed well under the cap.
        var stale = await _api.PlacedAsync("guard-cust-4", "guard-key-4a", 13); // 18850
        var staleId = stale.GetProperty("orderId").GetGuid();

        factory.Clock.Advance(TimeSpan.FromHours(26)); // new UTC day; draft TTL (200000s) not yet elapsed

        // Today: the same customer spends 37700 before confirming yesterday's draft.
        var b = await _api.PlacedAsync("guard-cust-4", "guard-key-4b", 13);
        await _api.ConfirmAsync(b.GetProperty("orderId").GetGuid(), "guard-cust-4");
        var c = await _api.PlacedAsync("guard-cust-4", "guard-key-4c", 13);
        await _api.ConfirmAsync(c.GetProperty("orderId").GetGuid(), "guard-cust-4");

        // 37700 today + 18850 = 56550 > 50000: the re-check at confirm fires.
        var confirm = await _api.ConfirmAsync(staleId, "guard-cust-4");
        confirm.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _api.WriteStatusAsync(staleId)).Should().Be("draft", "a failed re-check charges nothing and transitions nothing");
    }
}
