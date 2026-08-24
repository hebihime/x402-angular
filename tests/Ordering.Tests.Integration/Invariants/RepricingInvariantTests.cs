using System.Net;
using FluentAssertions;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>Invariant 1: the server is the only pricing authority.</summary>
[Collection("integration")]
public class RepricingInvariantTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    [Fact]
    public async Task Client_supplied_prices_totals_and_discounts_are_ignored_entirely()
    {
        // The request contract has no price fields; a hostile client injecting
        // them anyway changes nothing — the server reprices from the menu.
        var response = await _api.PlaceRawAsync("repricing-cust-1", "repricing-key-1", new
        {
            restaurantId = ApiDriver.PixelPizza,
            total = "1",
            discount = "9999",
            lines = new[]
            {
                new
                {
                    menuItemId = ApiDriver.Margherita,
                    quantity = 2,
                    modifierIds = new[] { ApiDriver.SizeLarge, ApiDriver.ExtraCheese },
                    unitPrice = "1",
                    lineTotal = "1",
                },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await ApiDriver.ReadJsonAsync(response);

        // Margherita 1195 + Large 350 + Extra cheese 200, x2 — from the menu, not the client.
        order.GetProperty("total").GetString().Should().Be("3490");
        var line = order.GetProperty("lines")[0];
        line.GetProperty("unitPrice").GetString().Should().Be("1195");
        line.GetProperty("lineTotal").GetString().Should().Be("3490");
        line.GetProperty("name").GetString().Should().Be("Margherita");
    }

    [Fact]
    public async Task Snapshots_are_locked_at_placement_and_survive_menu_changes()
    {
        var placed = await _api.PlacedAsync("repricing-cust-2", "repricing-key-2", quantity: 1);
        var orderId = placed.GetProperty("orderId").GetGuid();
        placed.GetProperty("total").GetString().Should().Be("1450");

        // The restaurant re-prices the Diavola afterwards.
        await _api.ExecuteAsync($"UPDATE menu_items SET base_price = 9999 WHERE id = '{ApiDriver.Diavola}'");
        try
        {
            var confirmed = await ApiDriver.ReadJsonAsync(await _api.ConfirmAsync(orderId, "repricing-cust-2"));
            confirmed.GetProperty("status").GetString().Should().Be("paid");
            confirmed.GetProperty("total").GetString().Should().Be("1450", "the total was locked at draft creation");

            // A new order sees the new price.
            var fresh = await _api.PlacedAsync("repricing-cust-2", "repricing-key-2b", quantity: 1);
            fresh.GetProperty("total").GetString().Should().Be("9999");
        }
        finally
        {
            await _api.ExecuteAsync($"UPDATE menu_items SET base_price = 1450 WHERE id = '{ApiDriver.Diavola}'");
        }
    }
}
