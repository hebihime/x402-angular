using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ordering.Mcp;

/// <summary>
/// MCP SDK wiring: tool registration, result envelopes, nothing else. The
/// tools themselves live on <see cref="OrderingTools"/> and are one fetch each.
/// </summary>
[McpServerToolType]
public sealed class OrderingMcpTools(OrderingTools tools)
{
    public static readonly string[] Names =
    [
        "list_restaurants",
        "get_menu",
        "place_order",
        "confirm_order",
        "cancel_order",
        "get_order_status",
    ];

    public static readonly string[] ReadOnlyNames =
    [
        "list_restaurants",
        "get_menu",
        "get_order_status",
    ];

    [McpServerTool(
        Name = "list_restaurants",
        Title = "List restaurants",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = true)]
    [Description(
        "Discover restaurants, optionally filtered by city (exact match on the city name). " +
        "Returns restaurant ids to use with get_menu and place_order.")]
    public Task<CallToolResult> ListRestaurants(
        [Description("Exact city name, e.g. \"Bangkok\". Omit for all cities.")] string? city = null,
        CancellationToken cancellationToken = default) =>
        Invoke(() => tools.ListRestaurantsAsync(city, cancellationToken));

    [McpServerTool(
        Name = "get_menu",
        Title = "Get menu",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = true)]
    [Description(
        "Read a restaurant's menu: items, prices, and one level of modifier groups " +
        "(each with min/max selections and per-modifier price deltas). Prices are integer " +
        "USD cents with a formatted string alongside. Prices you send when ordering are ignored; " +
        "the server always reprices from this menu.")]
    public Task<CallToolResult> GetMenu(
        [Description("Restaurant id from list_restaurants.")] Guid restaurantId,
        CancellationToken cancellationToken = default) =>
        Invoke(() => tools.GetMenuAsync(restaurantId, cancellationToken));

    [McpServerTool(
        Name = "place_order",
        Title = "Place order (draft, free)",
        ReadOnly = false,
        Idempotent = true,
        OpenWorld = true)]
    [Description(
        "Create a draft order. Free — no payment happens here. The server reprices every line from " +
        "the current menu, snapshots it, locks the total and sets an expiry; any price you send is " +
        "ignored. Pass the SAME idempotencyKey when retrying the same order: a repeat returns the " +
        "existing draft instead of creating a second one. A new key means a new order. " +
        "Pay the returned draft with confirm_order before it expires.")]
    public Task<CallToolResult> PlaceOrder(
        [Description("Restaurant id from list_restaurants.")] Guid restaurantId,
        [Description("Line items: menu item ids, quantities, and optional modifier ids from get_menu.")]
        IReadOnlyList<PlaceOrderLineInput> lines,
        [Description("Client-generated key. Reuse it verbatim on retry; a fresh key creates a new order.")]
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Invoke(() => tools.PlaceOrderAsync(restaurantId, lines, idempotencyKey, cancellationToken));

    [McpServerTool(
        Name = "confirm_order",
        Title = "Confirm and pay order",
        ReadOnly = false,
        Idempotent = true,
        OpenWorld = true)]
    [Description(
        "Pay a draft order with USDC on Base Sepolia via x402 and confirm it. This is the only " +
        "endpoint that charges: it answers the server's 402 payment challenge with a signed " +
        "X-PAYMENT for the exact locked total and retries. Safe to call again — a settled order " +
        "returns the original settlement and charges nothing. If no wallet is configured, the 402 " +
        "challenge is returned as data (paid: false) rather than thrown as an error.")]
    public Task<CallToolResult> ConfirmOrder(
        [Description("Draft order id from place_order.")] Guid orderId,
        CancellationToken cancellationToken = default) =>
        Invoke(() => tools.ConfirmOrderAsync(orderId, cancellationToken));

    [McpServerTool(
        Name = "cancel_order",
        Title = "Cancel order",
        ReadOnly = false,
        Idempotent = true,
        OpenWorld = true)]
    [Description(
        "Ask to cancel an order. The server decides whether the cancellation applies; " +
        "order.status is the state afterwards either way.")]
    public Task<CallToolResult> CancelOrder(
        [Description("Order id from place_order.")] Guid orderId,
        CancellationToken cancellationToken = default) =>
        Invoke(() => tools.CancelOrderAsync(orderId, cancellationToken));

    [McpServerTool(
        Name = "get_order_status",
        Title = "Get order status",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = true)]
    [Description(
        "Read an order and its full status history (each transition with the actor that caused it: " +
        "customer, restaurant, or system). Served from the read model — a just-placed draft may " +
        "404 until the projector catches up.")]
    public Task<CallToolResult> GetOrderStatus(
        [Description("Order id from place_order.")] Guid orderId,
        CancellationToken cancellationToken = default) =>
        Invoke(() => tools.GetOrderStatusAsync(orderId, cancellationToken));

    private static async Task<CallToolResult> Invoke(Func<Task<JsonElement>> call)
    {
        try
        {
            var json = await call();
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(json, JsonShape.Pretty) }],
            };
        }
        catch (ToolCallError error)
        {
            return new CallToolResult
            {
                IsError = true,
                Content =
                [
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(
                            new { error = error.Body, httpStatus = error.Status },
                            JsonShape.Pretty),
                    },
                ],
            };
        }
        catch (ArgumentException error)
        {
            return new CallToolResult
            {
                IsError = true,
                Content =
                [
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(
                            new { error = new { message = error.Message } },
                            JsonShape.Pretty),
                    },
                ],
            };
        }
    }
}
