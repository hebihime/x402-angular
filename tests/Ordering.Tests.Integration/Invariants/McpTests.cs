using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Payments;
using Ordering.Infrastructure.Persistence;
using Ordering.Infrastructure.Projections;
using Ordering.Mcp;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Phase 3: the MCP layer is a thin HTTP adapter. Tools run against the real
/// API factory; the only stand-in is the paying seam (fake X-PAYMENT on 402).
/// The point is to prove the MCP layer does NOT decide anything: it relays
/// what the server said, and a 402 arrives as data.
/// </summary>
[Collection("integration")]
public class McpTests(OrderingApiFactory factory)
{
    [Fact]
    public void Exposes_exactly_the_six_ordering_tools()
    {
        OrderingMcpTools.Names.Should().Equal(
            "list_restaurants",
            "get_menu",
            "place_order",
            "confirm_order",
            "cancel_order",
            "get_order_status");
        OrderingMcpTools.ReadOnlyNames.Should().Equal("list_restaurants", "get_menu", "get_order_status");
        typeof(OrderingMcpTools).GetCustomAttributes(false)
            .Select(a => a.GetType().Name)
            .Should().Contain("McpServerToolTypeAttribute");
    }

    [Fact]
    public async Task List_restaurants_filters_by_city()
    {
        var tools = CreateTools("mcp-list-1", canPay: false);
        var bangkok = await tools.ListRestaurantsAsync("Bangkok", CancellationToken.None);
        bangkok.GetProperty("restaurants").EnumerateArray().Select(r => r.GetProperty("name").GetString())
            .Should().Equal("Noodle Nexus", "Pixel Pizza");

        var all = await tools.ListRestaurantsAsync(null, CancellationToken.None);
        all.GetProperty("restaurants").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Get_menu_keeps_integer_cents_and_adds_a_display_string_next_to_each()
    {
        var tools = CreateTools("mcp-menu-1", canPay: false);
        var menu = await tools.GetMenuAsync(SeedData.PixelPizzaId, CancellationToken.None);
        var margherita = menu.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == SeedData.MargheritaId);

        margherita.GetProperty("basePrice").GetString().Should().Be("1195");
        margherita.GetProperty("basePriceDisplay").GetString().Should().Be("$11.95");

        var large = margherita.GetProperty("modifierGroups").EnumerateArray()
            .SelectMany(g => g.GetProperty("modifiers").EnumerateArray())
            .Single(m => m.GetProperty("id").GetGuid() == SeedData.PizzaSizeLargeId);
        large.GetProperty("priceDelta").GetString().Should().Be("350");
        large.GetProperty("priceDeltaDisplay").GetString().Should().Be("+$3.50");
    }

    [Fact]
    public async Task Place_order_sends_the_idempotency_key_and_returns_the_same_draft_on_retry()
    {
        var tools = CreateTools("mcp-place-1", canPay: false);
        const string key = "mcp-idem-1";
        var first = await DraftAsync(tools, key);
        var second = await DraftAsync(tools, key);

        second.GetProperty("order").GetProperty("orderId").GetGuid()
            .Should().Be(first.GetProperty("order").GetProperty("orderId").GetGuid());
        first.GetProperty("order").GetProperty("status").GetString().Should().Be("draft");
        second.GetProperty("order").GetProperty("total").GetString().Should().Be("1450");
        second.GetProperty("order").GetProperty("totalDisplay").GetString().Should().Be("$14.50");
    }

    [Fact]
    public async Task Place_order_refuses_to_call_the_API_without_an_idempotency_key()
    {
        var tools = CreateTools("mcp-place-2", canPay: false);
        var act = () => tools.PlaceOrderAsync(
            SeedData.PixelPizzaId,
            [new PlaceOrderLineInput { MenuItemId = SeedData.DiavolaId, Quantity = 1 }],
            idempotencyKey: "",
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*idempotencyKey*");
    }

    [Fact]
    public async Task Place_order_passes_only_ids_and_quantities_through()
    {
        var tools = CreateTools("mcp-place-3", canPay: false);
        var result = await tools.PlaceOrderAsync(
            SeedData.PixelPizzaId,
            [new PlaceOrderLineInput { MenuItemId = SeedData.DiavolaId, Quantity = 2 }],
            Guid.NewGuid().ToString("N"),
            CancellationToken.None);

        result.GetProperty("order").GetProperty("total").GetString().Should().Be("2900");
        result.GetProperty("order").GetProperty("totalDisplay").GetString().Should().Be("$29.00");
        result.GetProperty("order").GetProperty("lines")[0].GetProperty("unitPrice").GetString().Should().Be("1450");
    }

    [Fact]
    public async Task Confirm_answers_the_402_with_X_PAYMENT_and_returns_the_settlement()
    {
        var tools = CreateTools("mcp-confirm-1", canPay: true);
        var draft = await DraftAsync(tools);
        var result = await tools.ConfirmOrderAsync(
            draft.GetProperty("order").GetProperty("orderId").GetGuid(),
            CancellationToken.None);

        result.GetProperty("paid").GetBoolean().Should().BeTrue();
        result.GetProperty("order").GetProperty("status").GetString().Should().Be("paid");
        result.GetProperty("order").GetProperty("total").GetString().Should().Be("1450");
        result.GetProperty("order").GetProperty("totalDisplay").GetString().Should().Be("$14.50");
    }

    [Fact]
    public async Task Confirm_is_safe_to_call_twice_the_second_call_charges_nothing()
    {
        var tools = CreateTools("mcp-confirm-2", canPay: true);
        var orderId = (await DraftAsync(tools)).GetProperty("order").GetProperty("orderId").GetGuid();
        var first = await tools.ConfirmOrderAsync(orderId, CancellationToken.None);
        var second = await tools.ConfirmOrderAsync(orderId, CancellationToken.None);

        first.GetProperty("paid").GetBoolean().Should().BeTrue();
        second.GetProperty("paid").GetBoolean().Should().BeTrue();
        second.GetProperty("order").GetProperty("status").GetString().Should().Be("paid");

        var api = new ApiDriver(factory);
        (await api.ScalarAsync<int>("SELECT COUNT(*) FROM payments WHERE order_id = @orderId", new { orderId }))
            .Should().Be(1);
    }

    [Fact]
    public async Task Confirm_surfaces_the_payment_requirements_unpaid_when_no_wallet_is_configured()
    {
        var paying = CreateTools("mcp-confirm-3", canPay: true);
        var walletless = CreateTools("mcp-confirm-3", canPay: false);
        var orderId = (await DraftAsync(paying)).GetProperty("order").GetProperty("orderId").GetGuid();

        var result = await walletless.ConfirmOrderAsync(orderId, CancellationToken.None);
        result.GetProperty("paid").GetBoolean().Should().BeFalse();
        result.GetProperty("reason").GetString().Should().Be("no_wallet_configured");
        result.GetProperty("paymentRequirements")[0].GetProperty("scheme").GetString().Should().Be("exact");
        result.GetProperty("paymentRequirements")[0].GetProperty("network").GetString().Should().Be("base-sepolia");
        result.GetProperty("paymentRequirements")[0].GetProperty("maxAmountRequired").GetString()
            .Should().Be("14500000");

        var api = new ApiDriver(factory);
        (await api.WriteStatusAsync(orderId)).Should().Be("draft");
    }

    [Fact]
    public async Task Confirm_reports_payment_declined_when_the_paying_client_still_gets_a_402()
    {
        var tools = CreateTools("mcp-confirm-4", canPay: true);
        var orderId = (await DraftAsync(tools)).GetProperty("order").GetProperty("orderId").GetGuid();
        factory.Services.GetRequiredService<FakeFacilitator>().InjectVerifyFailures(1);

        var result = await tools.ConfirmOrderAsync(orderId, CancellationToken.None);
        result.GetProperty("paid").GetBoolean().Should().BeFalse();
        result.GetProperty("reason").GetString().Should().Be("payment_declined");
        result.GetProperty("paymentRequirements")[0].GetProperty("scheme").GetString().Should().Be("exact");

        var api = new ApiDriver(factory);
        (await api.WriteStatusAsync(orderId)).Should().Be("draft");
    }

    [Fact]
    public async Task Cancel_relays_the_order_the_server_returned()
    {
        var tools = CreateTools("mcp-cancel-1", canPay: false);
        var orderId = (await DraftAsync(tools)).GetProperty("order").GetProperty("orderId").GetGuid();

        var cancelled = await tools.CancelOrderAsync(orderId, CancellationToken.None);
        cancelled.GetProperty("order").GetProperty("status").GetString().Should().Be("cancelled");

        var again = await tools.CancelOrderAsync(orderId, CancellationToken.None);
        again.GetProperty("order").GetProperty("status").GetString().Should().Be("cancelled");
    }

    [Fact]
    public async Task Get_order_status_returns_history_after_the_projector_catches_up()
    {
        var tools = CreateTools("mcp-status-1", canPay: true);
        var orderId = (await DraftAsync(tools)).GetProperty("order").GetProperty("orderId").GetGuid();
        await tools.ConfirmOrderAsync(orderId, CancellationToken.None);
        await DrainProjectorAsync();

        var status = await tools.GetOrderStatusAsync(orderId, CancellationToken.None);
        status.GetProperty("order").GetProperty("status").GetString().Should().Be("paid");
        status.GetProperty("order").GetProperty("totalDisplay").GetString().Should().Be("$14.50");
        status.GetProperty("history").EnumerateArray()
            .Select(h => $"{h.GetProperty("to").GetString()}:{h.GetProperty("actor").GetString()}")
            .Should().Equal("draft:customer", "paid:system");
    }

    [Fact]
    public async Task Get_order_status_relays_a_404_for_an_unknown_order()
    {
        var tools = CreateTools("mcp-status-2", canPay: false);
        var act = () => tools.GetOrderStatusAsync(Guid.NewGuid(), CancellationToken.None);
        var error = await act.Should().ThrowAsync<ToolCallError>();
        error.Which.Status.Should().Be((int)HttpStatusCode.NotFound);
    }

    private OrderingTools CreateTools(string customerId, bool canPay)
    {
        var plain = factory.CreateClient();
        var paying = canPay
            ? factory.CreateDefaultClient(new ChallengeRetryHandler(new FakePayerHeaderProvider(ApiDriver.DefaultPayer)))
            : plain;
        return new OrderingTools(new ApiClient(plain, customerId, paying, canPay));
    }

    private static Task<JsonElement> DraftAsync(OrderingTools tools, string? idempotencyKey = null) =>
        tools.PlaceOrderAsync(
            SeedData.PixelPizzaId,
            [new PlaceOrderLineInput { MenuItemId = SeedData.DiavolaId, Quantity = 1 }],
            idempotencyKey ?? Guid.NewGuid().ToString("N"),
            CancellationToken.None);

    private async Task DrainProjectorAsync()
    {
        var processor = factory.Services.GetRequiredService<OutboxProjectionProcessor>();
        while (await processor.RunOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

}
