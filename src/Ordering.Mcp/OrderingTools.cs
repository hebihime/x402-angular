using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ordering.Mcp;

/// <summary>
/// The six agent tools. Each one is a single HTTP call plus response shaping.
/// If a tool contains an if-statement about order state, it is wrong.
/// </summary>
public sealed class OrderingTools(ApiClient client)
{
    public async Task<JsonElement> ListRestaurantsAsync(string? city, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(city) ? "" : $"?city={Uri.EscapeDataString(city)}";
        var body = Unwrap(await client.GetAsync($"api/restaurants{query}", cancellationToken));
        return JsonShape.ToElement(new JsonObject { ["restaurants"] = body.DeepClone() });
    }

    public async Task<JsonElement> GetMenuAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var body = Unwrap(await client.GetAsync($"api/restaurants/{restaurantId:D}/menu", cancellationToken));
        return JsonShape.ToElement(JsonShape.ShapeMenu(body));
    }

    public async Task<JsonElement> PlaceOrderAsync(
        Guid restaurantId,
        IReadOnlyList<PlaceOrderLineInput> lines,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "idempotencyKey is required. Reuse it verbatim on retry; the adapter never generates one.");
        }

        var payload = new
        {
            restaurantId,
            lines = lines.Select(l => new
            {
                menuItemId = l.MenuItemId,
                quantity = l.Quantity,
                modifierIds = l.ModifierIds ?? [],
            }),
        };

        var body = Unwrap(await client.PostAsync(
            "api/orders",
            JsonContent.Create(payload, options: JsonShape.Http),
            extraHeaders: new Dictionary<string, string> { [ApiClient.IdempotencyKeyHeader] = idempotencyKey },
            paying: false,
            cancellationToken));

        return JsonShape.ToElement(new JsonObject { ["order"] = JsonShape.ShapeOrder(body) });
    }

    public async Task<JsonElement> ConfirmOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(
            $"api/orders/{orderId:D}/confirm",
            content: null,
            extraHeaders: null,
            paying: true,
            cancellationToken);

        // A 402 here means the payment challenge was never answered: no wallet
        // is configured, or the paying client declined it. Relay the challenge
        // so the agent can pay out of band; this is transport, not order state.
        if (response.Status == 402)
        {
            var challenge = response.Body as JsonObject;
            var result = new JsonObject
            {
                ["paid"] = false,
                ["reason"] = client.CanPay ? "payment_declined" : "no_wallet_configured",
                ["error"] = challenge?["error"]?.DeepClone(),
                ["paymentRequirements"] = challenge?["accepts"]?.DeepClone(),
            };
            return JsonShape.ToElement(result);
        }

        var body = Unwrap(response);
        return JsonShape.ToElement(new JsonObject
        {
            ["paid"] = true,
            ["order"] = JsonShape.ShapeOrder(body),
        });
    }

    public async Task<JsonElement> CancelOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var body = Unwrap(await client.PostAsync(
            $"api/orders/{orderId:D}/cancel",
            content: null,
            extraHeaders: null,
            paying: false,
            cancellationToken));

        return JsonShape.ToElement(new JsonObject { ["order"] = JsonShape.ShapeOrder(body) });
    }

    public async Task<JsonElement> GetOrderStatusAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var body = Unwrap(await client.GetAsync($"api/orders/{orderId:D}", cancellationToken));
        var shaped = JsonShape.ShapeOrder(body);
        var result = new JsonObject
        {
            ["order"] = shaped,
            ["history"] = shaped["history"]?.DeepClone(),
        };
        return JsonShape.ToElement(result);
    }

    private static JsonNode Unwrap(ApiResponse response)
    {
        if (!response.Ok)
        {
            throw new ToolCallError(response.Status, response.Body);
        }

        return response.Body ?? new JsonObject();
    }
}

public sealed class PlaceOrderLineInput
{
    public Guid MenuItemId { get; set; }

    public int Quantity { get; set; }

    public IReadOnlyList<Guid>? ModifierIds { get; set; }
}
