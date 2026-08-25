using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ordering.Mcp;

/// <summary>
/// Response shaping: a display string next to every integer amount, never
/// instead of it, and flattening the API envelope. Deliberately absent:
/// deciding whether an order can be paid, retried, cancelled or refunded.
/// </summary>
public static class JsonShape
{
    public static readonly JsonSerializerOptions Http = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonObject ShapeOrder(JsonNode node)
    {
        var order = CloneObject(node);
        AddDisplay(order, "total", "totalDisplay");
        if (order["lines"] is JsonArray lines)
        {
            foreach (var item in lines)
            {
                if (item is not JsonObject line)
                {
                    continue;
                }

                AddDisplay(line, "unitPrice", "unitPriceDisplay");
                AddDisplay(line, "lineTotal", "lineTotalDisplay");
                if (line["modifiers"] is JsonArray modifiers)
                {
                    foreach (var modifierNode in modifiers)
                    {
                        if (modifierNode is JsonObject modifier)
                        {
                            AddDeltaDisplay(modifier, "priceDelta", "priceDeltaDisplay");
                        }
                    }
                }
            }
        }

        return order;
    }

    public static JsonObject ShapeMenu(JsonNode node)
    {
        var menu = CloneObject(node);
        if (menu["items"] is JsonArray items)
        {
            foreach (var itemNode in items)
            {
                if (itemNode is not JsonObject item)
                {
                    continue;
                }

                AddDisplay(item, "basePrice", "basePriceDisplay");
                if (item["modifierGroups"] is JsonArray groups)
                {
                    foreach (var groupNode in groups)
                    {
                        if (groupNode is not JsonObject group || group["modifiers"] is not JsonArray modifiers)
                        {
                            continue;
                        }

                        foreach (var modifierNode in modifiers)
                        {
                            if (modifierNode is JsonObject modifier)
                            {
                                AddDeltaDisplay(modifier, "priceDelta", "priceDeltaDisplay");
                            }
                        }
                    }
                }
            }
        }

        return menu;
    }

    public static JsonElement ToElement(JsonNode node) =>
        JsonSerializer.SerializeToElement(node, Pretty);

    private static JsonObject CloneObject(JsonNode node) =>
        node.DeepClone() as JsonObject
        ?? throw new InvalidOperationException("Expected a JSON object from the API.");

    private static void AddDisplay(JsonObject obj, string amountProperty, string displayProperty)
    {
        if (TryReadMinorUnits(obj, amountProperty, out var minorUnits))
        {
            obj[displayProperty] = MoneyFormat.Usd(minorUnits);
        }
    }

    private static void AddDeltaDisplay(JsonObject obj, string amountProperty, string displayProperty)
    {
        if (TryReadMinorUnits(obj, amountProperty, out var minorUnits))
        {
            obj[displayProperty] = MoneyFormat.UsdDelta(minorUnits);
        }
    }

    private static bool TryReadMinorUnits(JsonObject obj, string property, out string minorUnits)
    {
        minorUnits = "";
        if (obj[property] is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<string>(out var asString) && asString is not null)
        {
            minorUnits = asString;
            return true;
        }

        return false;
    }
}
