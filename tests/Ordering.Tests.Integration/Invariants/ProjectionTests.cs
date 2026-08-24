using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Projections;

namespace Ordering.Tests.Integration.Invariants;

/// <summary>
/// Invariant 8: read/write separation is real. Commands land in the outbox;
/// the projector drains it in order into the Dapper-only projection tables and
/// then broadcasts; queries serve exclusively from those tables.
/// </summary>
[Collection("integration")]
public class ProjectionTests(OrderingApiFactory factory)
{
    private readonly ApiDriver _api = new(factory);

    private async Task RunProjectorAsync()
    {
        // Drain fully: earlier tests in the collection may have left more than
        // one batch of unprocessed rows.
        var processor = factory.Services.GetRequiredService<OutboxProjectionProcessor>();
        while (await processor.RunOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

    [Fact]
    public async Task A_commands_event_reaches_the_projection_and_the_read_api()
    {
        var placed = await _api.PlacedAsync("proj-cust-1", "proj-key-1", quantity: 2);
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "proj-cust-1");

        // Not yet projected: the read side must not see the order (eventual
        // consistency is embraced, not patched over by falling back to the
        // write model).
        var beforeCount = await _api.ScalarAsync<int>(
            "SELECT COUNT(*) FROM read_orders WHERE order_id = @orderId", new { orderId });
        beforeCount.Should().Be(0);
        (await _api.Client.GetAsync($"/api/orders/{orderId}")).StatusCode
            .Should().Be(System.Net.HttpStatusCode.NotFound);

        await RunProjectorAsync();

        var details = await ApiDriver.ReadJsonAsync(await _api.Client.GetAsync($"/api/orders/{orderId}"));
        details.GetProperty("status").GetString().Should().Be("paid");
        details.GetProperty("total").GetString().Should().Be("2900");
        details.GetProperty("lines")[0].GetProperty("name").GetString().Should().Be("Diavola");
        details.GetProperty("lines")[0].GetProperty("lineTotal").GetString().Should().Be("2900");
        details.GetProperty("history").GetArrayLength().Should().Be(2);
        details.GetProperty("history")[0].GetProperty("to").GetString().Should().Be("draft");
        details.GetProperty("history")[1].GetProperty("to").GetString().Should().Be("paid");
        details.GetProperty("history")[1].GetProperty("actor").GetString().Should().Be("system");
    }

    [Fact]
    public async Task The_outbox_drains_in_order_marks_processed_and_broadcasts_after_commit()
    {
        while (factory.Notifier.Events.TryDequeue(out _))
        {
        }

        var placed = await _api.PlacedAsync("proj-cust-2", "proj-key-2");
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "proj-cust-2");
        await _api.DashboardAsync(orderId, "accept");

        await RunProjectorAsync();

        (await _api.ScalarAsync<int>(
            "SELECT COUNT(*) FROM outbox WHERE order_id = @orderId AND processed_at IS NULL", new { orderId }))
            .Should().Be(0, "every drained row is flagged processed");

        var broadcastsForOrder = factory.Notifier.Events.Where(e => e.Order.OrderId == orderId).ToList();
        broadcastsForOrder.Select(e => e.Order.Status).Should().ContainInOrder("draft", "paid", "accepted");
        broadcastsForOrder.Select(e => e.EventType).Should().ContainInOrder(
            "OrderPlaced", "OrderStatusChanged", "OrderStatusChanged");
        broadcastsForOrder.Should().OnlyContain(e => e.Order.Total.MinorUnits == 1450);

        // Replaying the projector is harmless: nothing unprocessed remains.
        (await factory.Services.GetRequiredService<OutboxProjectionProcessor>()
            .RunOnceAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task The_board_query_filters_by_status_from_the_projection()
    {
        var placed = await _api.PlacedAsync("proj-cust-3", "proj-key-3");
        var orderId = placed.GetProperty("orderId").GetGuid();
        await _api.ConfirmAsync(orderId, "proj-cust-3");
        await RunProjectorAsync();

        var paid = await ApiDriver.ReadJsonAsync(await _api.Client.GetAsync(
            $"/api/restaurants/{ApiDriver.PixelPizza}/orders?status=paid"));
        paid.EnumerateArray().Select(o => o.GetProperty("orderId").GetGuid()).Should().Contain(orderId);

        var completed = await ApiDriver.ReadJsonAsync(await _api.Client.GetAsync(
            $"/api/restaurants/{ApiDriver.PixelPizza}/orders?status=completed"));
        completed.EnumerateArray().Select(o => o.GetProperty("orderId").GetGuid()).Should().NotContain(orderId);

        (await _api.Client.GetAsync($"/api/restaurants/{ApiDriver.PixelPizza}/orders?status=bogus"))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Menus_and_restaurants_serve_from_the_read_tables()
    {
        var menu = await ApiDriver.ReadJsonAsync(await _api.Client.GetAsync(
            $"/api/restaurants/{ApiDriver.PixelPizza}/menu"));
        menu.GetProperty("restaurantName").GetString().Should().Be("Pixel Pizza");
        var items = menu.GetProperty("items").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("name").GetString() == "Margherita");
        var margherita = items.Single(i => i.GetProperty("name").GetString() == "Margherita");
        margherita.GetProperty("basePrice").GetString().Should().Be("1195");
        margherita.GetProperty("modifierGroups").GetArrayLength().Should().Be(2);

        var bangkok = await ApiDriver.ReadJsonAsync(await _api.Client.GetAsync("/api/restaurants?city=Bangkok"));
        bangkok.EnumerateArray().Select(r => r.GetProperty("name").GetString())
            .Should().BeEquivalentTo("Pixel Pizza", "Noodle Nexus");
    }
}
