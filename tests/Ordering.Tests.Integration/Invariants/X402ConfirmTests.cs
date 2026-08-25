using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Payments;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Phase 1: confirm is the 402 handshake. No header → protocol challenge from
/// the locked total; a replayed X-PAYMENT returns the original success and
/// never hits the facilitator; payload/tx hashes are unique.
/// </summary>
[Collection("integration")]
public class X402ConfirmTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);
    private FakeFacilitator Facilitator => factory.Services.GetRequiredService<FakeFacilitator>();

    [Fact]
    public async Task Confirm_without_X_PAYMENT_returns_the_protocol_402_not_problem_details()
    {
        var placed = await _api.PlacedAsync("x402-cust-1", "x402-key-1");
        var orderId = placed.GetProperty("orderId").GetGuid();
        var verifiesBefore = Facilitator.Verifies.Count;

        var response = await _api.ConfirmWithoutPaymentAsync(orderId, "x402-cust-1");
        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var body = await ApiDriver.ReadJsonAsync(response);
        body.TryGetProperty("title", out _).Should().BeFalse("this is not ProblemDetails");
        body.GetProperty("x402Version").GetInt32().Should().Be(1);
        body.GetProperty("error").GetString().Should().Contain("X-PAYMENT");

        var accept = body.GetProperty("accepts")[0];
        accept.GetProperty("scheme").GetString().Should().Be("exact");
        accept.GetProperty("network").GetString().Should().Be("base-sepolia");
        accept.GetProperty("maxAmountRequired").GetString().Should().Be("14500000", "1450 cents × 10_000 atomic USDC");
        accept.GetProperty("payTo").GetString().Should().StartWith("0x");
        accept.GetProperty("asset").GetString().Should().StartWith("0x");
        accept.GetProperty("resource").GetString().Should().Contain($"/api/orders/{orderId}/confirm");
        accept.GetProperty("extra").GetProperty("name").GetString().Should().Be("USDC");

        (await _api.WriteStatusAsync(orderId)).Should().Be("draft");
        Facilitator.Verifies.Count.Should().Be(verifiesBefore, "no header means no facilitator");
    }

    [Fact]
    public async Task Confirm_ignores_a_client_supplied_amount_in_the_body()
    {
        var placed = await _api.PlacedAsync("x402-cust-2", "x402-key-2");
        var orderId = placed.GetProperty("orderId").GetGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/confirm")
        {
            Content = JsonContent.Create(new { amount = "1", total = "1" }),
        };
        request.Headers.Add("X-Customer-Id", "x402-cust-2");
        request.Headers.Add("X-PAYMENT", FakeFacilitator.EncodePaymentHeader(ApiDriver.DefaultPayer, orderId.ToString("N")));

        var paid = await ApiDriver.ReadJsonAsync(await _api.Client.SendAsync(request));
        paid.GetProperty("status").GetString().Should().Be("paid");
        paid.GetProperty("total").GetString().Should().Be("1450");

        var amount = await _api.ScalarAsync<long>("SELECT amount_minor_units FROM payments WHERE order_id = @orderId", new { orderId });
        amount.Should().Be(1450);
    }

    [Fact]
    public async Task Replayed_X_PAYMENT_returns_the_original_success_and_does_not_hit_the_facilitator_again()
    {
        var placed = await _api.PlacedAsync("x402-cust-3", "x402-key-3");
        var orderId = placed.GetProperty("orderId").GetGuid();
        var header = FakeFacilitator.EncodePaymentHeader(ApiDriver.DefaultPayer, orderId.ToString("N"));

        var verifiesBefore = Facilitator.Verifies.Count;
        (await _api.ConfirmAsync(orderId, "x402-cust-3", header)).EnsureSuccessStatusCode();
        var verifiesAfterFirst = Facilitator.Verifies.Count;
        verifiesAfterFirst.Should().Be(verifiesBefore + 1);

        var replay = await ApiDriver.ReadJsonAsync(await _api.ConfirmAsync(orderId, "x402-cust-3", header));
        replay.GetProperty("status").GetString().Should().Be("paid");
        Facilitator.Verifies.Count.Should().Be(verifiesAfterFirst, "already-paid confirm never hits the facilitator");
        Facilitator.Settles.Count(s => s.Header == header).Should().Be(1);

        (await _api.ScalarAsync<int>("SELECT COUNT(*) FROM payments WHERE order_id = @orderId", new { orderId }))
            .Should().Be(1);
    }

    [Fact]
    public async Task The_same_payment_payload_cannot_settle_a_second_order()
    {
        var a = await _api.PlacedAsync("x402-cust-4a", "x402-key-4a");
        var b = await _api.PlacedAsync("x402-cust-4b", "x402-key-4b");
        var aId = a.GetProperty("orderId").GetGuid();
        var bId = b.GetProperty("orderId").GetGuid();
        var header = FakeFacilitator.EncodePaymentHeader(ApiDriver.DefaultPayer, "shared-nonce");

        (await _api.ConfirmAsync(aId, "x402-cust-4a", header)).EnsureSuccessStatusCode();

        var second = await _api.ConfirmAsync(bId, "x402-cust-4b", header);
        second.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await _api.WriteStatusAsync(bId)).Should().Be("draft");
        (await _api.ScalarAsync<int>("SELECT COUNT(*) FROM payments")).Should().BeGreaterThanOrEqualTo(1);
        (await _api.ScalarAsync<int>("SELECT COUNT(*) FROM payments WHERE order_id = @orderId", new { orderId = bId }))
            .Should().Be(0);
    }

    [Fact]
    public async Task Already_paid_confirm_without_a_header_is_still_the_original_success()
    {
        var placed = await _api.PlacedAsync("x402-cust-5", "x402-key-5");
        var orderId = placed.GetProperty("orderId").GetGuid();
        (await _api.ConfirmAsync(orderId, "x402-cust-5")).EnsureSuccessStatusCode();

        var replay = await ApiDriver.ReadJsonAsync(await _api.ConfirmWithoutPaymentAsync(orderId, "x402-cust-5"));
        replay.GetProperty("status").GetString().Should().Be("paid");
    }

    [Fact]
    public async Task Fake_facilitator_can_fail_N_verifies_then_succeed()
    {
        var placed = await _api.PlacedAsync("x402-cust-6", "x402-key-6");
        var orderId = placed.GetProperty("orderId").GetGuid();
        Facilitator.InjectVerifyFailures(2);

        (await _api.ConfirmAsync(orderId, "x402-cust-6")).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await _api.ConfirmAsync(orderId, "x402-cust-6")).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await _api.WriteStatusAsync(orderId)).Should().Be("draft");

        var paid = await ApiDriver.ReadJsonAsync(await _api.ConfirmAsync(orderId, "x402-cust-6"));
        paid.GetProperty("status").GetString().Should().Be("paid");
    }

    [Fact]
    public async Task A_malformed_X_PAYMENT_is_a_protocol_402()
    {
        var placed = await _api.PlacedAsync("x402-cust-7", "x402-key-7");
        var orderId = placed.GetProperty("orderId").GetGuid();

        var response = await _api.ConfirmAsync(orderId, "x402-cust-7", "not-valid-base64-or-json");
        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var body = await ApiDriver.ReadJsonAsync(response);
        body.GetProperty("accepts")[0].GetProperty("scheme").GetString().Should().Be("exact");
        (await _api.WriteStatusAsync(orderId)).Should().Be("draft");
    }
}
