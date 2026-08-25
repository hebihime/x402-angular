using System.Net.Http.Json;
using System.Text.Json;

namespace Ordering.Mcp;

/// <summary>
/// Phase 5: the agent story through the real MCP tools against a live API
/// (fake facilitator + fake refund rail). Kitchen reject is dashboard HTTP —
/// it is not an MCP tool. Exits non-zero if any beat is wrong.
/// </summary>
internal static class DemoRunner
{
    private const string DefaultPayer = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var customerId = McpRuntime.CustomerId;
        var payer = McpRuntime.FakePayer ?? DefaultPayer;
        var checks = new List<string>();

        void Check(bool condition, string claim)
        {
            if (!condition)
            {
                throw new DemoFailedException(claim);
            }

            checks.Add(claim);
            Console.WriteLine($"   ✓ {claim}");
        }

        void Step(string title) => Console.WriteLine($"\n» {title}");
        void Info(string message) => Console.WriteLine($"   · {message}");

        try
        {
            using var kitchen = new HttpClient { BaseAddress = McpRuntime.BaseAddress };
            var agent = McpRuntime.CreateTools(customerId, fakePayer: null);
            var paying = McpRuntime.CreateTools(customerId, payer);

            Step("Agent discovers a restaurant and reads the menu (MCP)");
            var listed = await agent.ListRestaurantsAsync("Bangkok", cancellationToken);
            var pixel = listed.GetProperty("restaurants").EnumerateArray()
                .First(r => r.GetProperty("name").GetString() == "Pixel Pizza");
            var restaurantId = pixel.GetProperty("id").GetGuid();
            Info($"list_restaurants(city=Bangkok) → {pixel.GetProperty("name").GetString()} {restaurantId:D}");

            var menu = await agent.GetMenuAsync(restaurantId, cancellationToken);
            var margherita = FindByName(menu.GetProperty("items"), "Margherita");
            var large = FindModifier(margherita, "Large");
            var extraCheese = FindModifier(margherita, "Extra cheese");
            Info($"get_menu → Margherita {margherita.GetProperty("basePriceDisplay").GetString()}, Large {large.GetProperty("priceDeltaDisplay").GetString()}");
            Check(
                margherita.GetProperty("basePrice").GetString() == "1195",
                "menu keeps integer cents beside the display string");

            Step("place_order — twice with the same idempotencyKey");
            var key = $"demo-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var lines = new PlaceOrderLineInput[]
            {
                new()
                {
                    MenuItemId = margherita.GetProperty("id").GetGuid(),
                    Quantity = 2,
                    ModifierIds = [large.GetProperty("id").GetGuid(), extraCheese.GetProperty("id").GetGuid()],
                },
            };
            var first = await agent.PlaceOrderAsync(restaurantId, lines, key, cancellationToken);
            var orderId = first.GetProperty("order").GetProperty("orderId").GetGuid();
            var total = first.GetProperty("order").GetProperty("total").GetString();
            Info($"draft {orderId:D} · total {total} ({first.GetProperty("order").GetProperty("totalDisplay").GetString()}) locked");
            Check(first.GetProperty("order").GetProperty("status").GetString() == "draft", "place_order created a free draft");
            Check(total == "3490", "server repriced Margherita+Large+Extra cheese ×2 to 3490 cents");

            var replayPlace = await agent.PlaceOrderAsync(
                restaurantId,
                [new PlaceOrderLineInput { MenuItemId = lines[0].MenuItemId, Quantity = 1, ModifierIds = [] }],
                key,
                cancellationToken);
            Check(
                replayPlace.GetProperty("order").GetProperty("orderId").GetGuid() == orderId,
                "replayed place_order returned the SAME draft — one order, not two");
            Check(
                replayPlace.GetProperty("order").GetProperty("total").GetString() == "3490",
                "the original locked total wins, not the retried body");

            Step("confirm_order — 402 challenge, then pay");
            var unpaid = await agent.ConfirmOrderAsync(orderId, cancellationToken);
            Check(unpaid.GetProperty("paid").GetBoolean() == false, "confirm without a wallet returns 402 as data, not an error");
            Check(unpaid.GetProperty("reason").GetString() == "no_wallet_configured", "reason is no_wallet_configured");
            var required = unpaid.GetProperty("paymentRequirements")[0].GetProperty("maxAmountRequired").GetString();
            Info($"402 maxAmountRequired={required} (USDC atomic)");
            Check(required == "34900000", "the 402 asked for the locked total (3490 cents × 10_000), not an agent price");

            var paid = await paying.ConfirmOrderAsync(orderId, cancellationToken);
            Check(paid.GetProperty("paid").GetBoolean(), "paying client answered the 402");
            Check(paid.GetProperty("order").GetProperty("status").GetString() == "paid", "order settled: draft → paid");
            Info($"paid · {paid.GetProperty("order").GetProperty("totalDisplay").GetString()}");

            Step("Replaying confirm settles nothing");
            var replayPay = await paying.ConfirmOrderAsync(orderId, cancellationToken);
            Check(replayPay.GetProperty("paid").GetBoolean(), "replayed confirm is the original success");
            Check(
                replayPay.GetProperty("order").GetProperty("status").GetString() == "paid",
                "still paid — the facilitator was not needed for a second settlement");

            Step("Inject two refund-rail failures, then the kitchen rejects");
            var injected = await kitchen.PostAsJsonAsync("api/demo/gateway/fail-refunds", new { count = 2 }, cancellationToken);
            injected.EnsureSuccessStatusCode();
            var rejected = await kitchen.PostAsJsonAsync(
                $"api/restaurants/{restaurantId:D}/orders/{orderId:D}/reject",
                new { reason = "demo: out of dough" },
                cancellationToken);
            rejected.EnsureSuccessStatusCode();
            using var rejectedBody = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(cancellationToken));
            Check(
                rejectedBody.RootElement.GetProperty("status").GetString() == "refund_pending",
                "rejected → refund_pending in the same transaction");

            Step("Refund worker: two injected failures, backoff, then the money goes back");
            Info("x402 settlement is irreversible — the refund is a NEW transfer to the payer");
            JsonElement? final = null;
            string? lastLine = null;
            for (var i = 0; i < 60; i++)
            {
                try
                {
                    var status = await paying.GetOrderStatusAsync(orderId, cancellationToken);
                    var wire = status.GetProperty("order").GetProperty("status").GetString();
                    var attempts = status.GetProperty("order").GetProperty("refundAttempts").GetInt32();
                    var line = $"status={wire} refundAttempts={attempts}";
                    if (line != lastLine)
                    {
                        Info(line);
                        lastLine = line;
                    }

                    if (wire == "paid")
                    {
                        Info("(read-model lag: projector has not caught the rejection yet)");
                    }

                    if (wire == "refunded")
                    {
                        final = status;
                        break;
                    }
                }
                catch (ToolCallError error) when (error.Status == 404)
                {
                    Info("read-model lag: order not projected yet");
                }

                await Task.Delay(1000, cancellationToken);
            }

            Check(final is not null, "order reached refunded after the two injected transfer failures");
            var done = final!.Value;
            Check(done.GetProperty("order").GetProperty("refundAttempts").GetInt32() == 2, "two failed attempts recorded; the success is not a failure");
            Check(done.GetProperty("order").GetProperty("status").GetString() == "refunded", "order is refunded");

            Step("Full status history");
            var trail = done.GetProperty("history").EnumerateArray()
                .Select(h => $"{h.GetProperty("to").GetString()}:{h.GetProperty("actor").GetString()}")
                .ToArray();
            foreach (var h in done.GetProperty("history").EnumerateArray())
            {
                var from = h.GetProperty("from").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    ? "∅"
                    : h.GetProperty("from").GetString();
                var reason = h.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? $" — {r.GetString()}"
                    : "";
                Console.WriteLine($"   {(from ?? "∅"),14} → {h.GetProperty("to").GetString(),-14} [{h.GetProperty("actor").GetString()}]{reason}");
            }

            Check(
                string.Join(",", trail) == "draft:customer,paid:system,rejected:restaurant,refund_pending:system,refunded:system",
                "history is the whole saga: customer drafted, system settled, restaurant rejected, system refunded");

            Console.WriteLine($"\nDemo complete: {checks.Count} assertions passed.");
            return 0;
        }
        catch (DemoFailedException ex)
        {
            Console.Error.WriteLine($"✗ {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ demo crashed: {ex.Message}");
            return 1;
        }
    }

    private static JsonElement FindByName(JsonElement items, string name) =>
        items.EnumerateArray().First(i => i.GetProperty("name").GetString() == name);

    private static JsonElement FindModifier(JsonElement item, string name) =>
        item.GetProperty("modifierGroups").EnumerateArray()
            .SelectMany(g => g.GetProperty("modifiers").EnumerateArray())
            .First(m => m.GetProperty("name").GetString() == name);
}

internal sealed class DemoFailedException(string claim) : Exception(claim);
