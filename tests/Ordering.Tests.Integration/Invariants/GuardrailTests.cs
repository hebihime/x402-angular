using System.Net;
using FluentAssertions;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Invariant 6: max-order at draft and confirm; daily cap at draft keys on
/// X-Customer-Id, at confirm on the verified payer. Test config: max order
/// 20000, daily cap 50000; Diavola is 1450.
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
    public async Task Confirm_rechecks_the_daily_cap_against_the_verified_payer()
    {
        const string payer = "0xcccccccccccccccccccccccccccccccccccccccc";

        // Yesterday: a draft placed well under the cap.
        var stale = await _api.PlacedAsync("guard-cust-4", "guard-key-4a", 13); // 18850
        var staleId = stale.GetProperty("orderId").GetGuid();

        factory.Clock.Advance(TimeSpan.FromHours(26)); // new UTC day; draft TTL (200000s) not yet elapsed

        // Today: the same payer settles 37700 before confirming yesterday's draft.
        var b = await _api.PlacedAsync("guard-cust-4", "guard-key-4b", 13);
        var bId = b.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(bId, "guard-cust-4", ApiDriver.PaymentHeader(payer, bId))).EnsureSuccessStatusCode();
        var c = await _api.PlacedAsync("guard-cust-4", "guard-key-4c", 13);
        var cId = c.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(cId, "guard-cust-4", ApiDriver.PaymentHeader(payer, cId))).EnsureSuccessStatusCode();

        // 37700 today + 18850 = 56550 > 50000: the re-check at confirm fires.
        var confirm = await _api.ConfirmAsync(staleId, "guard-cust-4", ApiDriver.PaymentHeader(payer, staleId));
        confirm.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _api.WriteStatusAsync(staleId)).Should().Be("draft", "a failed re-check settles nothing and transitions nothing");
    }

    [Fact]
    public async Task Two_payers_do_not_share_the_daily_cap()
    {
        const string payerA = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa01";
        const string payerB = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb01";

        var a = await _api.PlacedAsync("guard-cust-5a", "guard-key-5a", 13); // 18850
        var aId = a.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(aId, "guard-cust-5a", ApiDriver.PaymentHeader(payerA, aId))).EnsureSuccessStatusCode();

        var b = await _api.PlacedAsync("guard-cust-5b", "guard-key-5b", 13);
        var bId = b.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(bId, "guard-cust-5b", ApiDriver.PaymentHeader(payerA, bId))).EnsureSuccessStatusCode();

        // Payer A is at 37700 of 50000; payer B is untouched.
        var other = await _api.PlacedAsync("guard-cust-5c", "guard-key-5c", 13);
        var otherId = other.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(otherId, "guard-cust-5c", ApiDriver.PaymentHeader(payerB, otherId)))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task X_Customer_Id_cannot_spoof_the_payer_daily_cap()
    {
        const string payer = "0xdddddddddddddddddddddddddddddddddddddd01";

        var a = await _api.PlacedAsync("guard-cust-6a", "guard-key-6a", 13);
        var aId = a.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(aId, "guard-cust-6a", ApiDriver.PaymentHeader(payer, aId))).EnsureSuccessStatusCode();

        var b = await _api.PlacedAsync("guard-cust-6b", "guard-key-6b", 13);
        var bId = b.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(bId, "guard-cust-6b", ApiDriver.PaymentHeader(payer, bId))).EnsureSuccessStatusCode();

        var c = await _api.PlacedAsync("guard-cust-6c", "guard-key-6c", 13);
        var cId = c.GetProperty("orderId").GetGuid();
        var blocked = await _api.ConfirmAsync(cId, "guard-cust-6c", ApiDriver.PaymentHeader(payer, cId));
        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _api.WriteStatusAsync(cId)).Should().Be("draft");
    }

    [Fact]
    public async Task Get_guardrails_publishes_the_effective_limits()
    {
        var body = await ApiDriver.ReadJsonAsync(await _api.Client.GetAsync("/api/guardrails"));
        body.GetProperty("maxOrderValueMinorUnits").GetString().Should().Be("20000");
        body.GetProperty("dailySpendCapMinorUnits").GetString().Should().Be("50000");
        body.GetProperty("dailySpendCapWindow").GetString().Should().Be("utc_day");
        body.GetProperty("draftTtlSeconds").GetInt32().Should().Be(200000);
        body.GetProperty("network").GetString().Should().Be("base-sepolia");
        body.GetProperty("asset").GetString().Should().StartWith("0x");
        body.GetProperty("payTo").GetString().Should().Be("0x0000000000000000000000000000000000000001");
    }
}
