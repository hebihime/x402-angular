using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Ordering.Infrastructure.Payments;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Tests.Integration;

/// <summary>
/// Drives the API through the wire contract (raw JSON, not shared DTOs) and
/// inspects the database directly for invariant assertions.
/// </summary>
public sealed class ApiDriver(OrderingApiFactory factory)
{
    public HttpClient Client { get; } = factory.CreateClient();

    public static readonly Guid PixelPizza = SeedData.PixelPizzaId;
    public static readonly Guid Diavola = SeedData.DiavolaId;
    public static readonly Guid Margherita = SeedData.MargheritaId;
    public static readonly Guid SizeSmall = SeedData.PizzaSizeSmallId;
    public static readonly Guid SizeLarge = SeedData.PizzaSizeLargeId;
    public static readonly Guid ExtraCheese = SeedData.ToppingExtraCheeseId;

    public async Task<HttpResponseMessage> PlaceRawAsync(string customerId, string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Customer-Id", customerId);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await Client.SendAsync(request);
    }

    /// <summary>Places qty x Diavola (1450 each, no modifier groups).</summary>
    public Task<HttpResponseMessage> PlaceDiavolaAsync(string customerId, string idempotencyKey, int quantity) =>
        PlaceRawAsync(customerId, idempotencyKey, new
        {
            restaurantId = PixelPizza,
            lines = new[] { new { menuItemId = Diavola, quantity, modifierIds = Array.Empty<Guid>() } },
        });

    public async Task<JsonElement> PlacedAsync(string customerId, string idempotencyKey, int quantity = 1)
    {
        var response = await PlaceDiavolaAsync(customerId, idempotencyKey, quantity);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    public const string DefaultPayer = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public async Task<HttpResponseMessage> ConfirmAsync(Guid orderId, string customerId, string? paymentHeader = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/confirm");
        request.Headers.Add("X-Customer-Id", customerId);
        request.Headers.Add(
            "X-PAYMENT",
            paymentHeader ?? FakeFacilitator.EncodePaymentHeader(DefaultPayer, orderId.ToString("N")));
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> ConfirmWithoutPaymentAsync(Guid orderId, string customerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/confirm");
        request.Headers.Add("X-Customer-Id", customerId);
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> CancelAsync(Guid orderId, string customerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel");
        request.Headers.Add("X-Customer-Id", customerId);
        return await Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> DashboardAsync(Guid orderId, string action, object? body = null) =>
        Client.PostAsync($"/api/restaurants/{PixelPizza}/orders/{orderId}/{action}",
            body is null ? JsonContent.Create(new { }) : JsonContent.Create(body));

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    // --- direct database inspection ---

    private readonly string _connectionString = factory.ConnectionString;

    public async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql);
    }

    public Task<int> HistoryCountAsync(Guid orderId) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM status_history WHERE order_id = @orderId", new { orderId });

    public Task<int> OutboxCountAsync(Guid orderId) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM outbox WHERE order_id = @orderId", new { orderId });

    public Task<string> WriteStatusAsync(Guid orderId) =>
        ScalarAsync<string>("SELECT status FROM orders WHERE id = @orderId", new { orderId });
}
