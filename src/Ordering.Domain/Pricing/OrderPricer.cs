using Ordering.Domain.Catalog;
using Ordering.Domain.Orders;

namespace Ordering.Domain.Pricing;

/// <summary>What a customer asks for: item ids and quantities only. Prices never come from the client.</summary>
public sealed record RequestedLine(Guid MenuItemId, int Quantity, IReadOnlyList<Guid> ModifierIds);

public sealed record PricingResult(bool Success, IReadOnlyList<OrderLine> Lines, Money Total, string? Error)
{
    public static PricingResult Ok(IReadOnlyList<OrderLine> lines, Money total) => new(true, lines, total, null);
    public static PricingResult Fail(string error) => new(false, [], Money.Zero, error);
}

/// <summary>
/// The single pricing authority. Reprices every requested line from the
/// current menu, applies modifier deltas, validates modifier-group min/max
/// counts, and produces the immutable line snapshots and locked total.
/// </summary>
public static class OrderPricer
{
    public static PricingResult Price(Restaurant restaurant, IReadOnlyList<RequestedLine> requestedLines)
    {
        if (requestedLines.Count == 0)
        {
            return PricingResult.Fail("An order must contain at least one line.");
        }

        var menuItemsById = restaurant.MenuItems.ToDictionary(mi => mi.Id);
        var snapshots = new List<OrderLine>(requestedLines.Count);
        var total = Money.Zero;

        foreach (var requested in requestedLines)
        {
            if (requested.Quantity < 1)
            {
                return PricingResult.Fail($"Quantity must be at least 1 (menu item {requested.MenuItemId}).");
            }

            if (!menuItemsById.TryGetValue(requested.MenuItemId, out var menuItem))
            {
                return PricingResult.Fail($"Menu item {requested.MenuItemId} does not exist on this restaurant's menu.");
            }

            if (requested.ModifierIds.Distinct().Count() != requested.ModifierIds.Count)
            {
                return PricingResult.Fail($"Duplicate modifiers requested for '{menuItem.Name}'.");
            }

            var modifiersById = menuItem.ModifierGroups
                .SelectMany(g => g.Modifiers)
                .ToDictionary(m => m.Id);

            var lineModifiers = new List<OrderLineModifier>(requested.ModifierIds.Count);
            foreach (var modifierId in requested.ModifierIds)
            {
                if (!modifiersById.TryGetValue(modifierId, out var modifier))
                {
                    return PricingResult.Fail($"Modifier {modifierId} does not belong to '{menuItem.Name}'.");
                }

                lineModifiers.Add(new OrderLineModifier(modifier.Id, modifier.Name, modifier.PriceDelta));
            }

            foreach (var group in menuItem.ModifierGroups)
            {
                var selectedInGroup = group.Modifiers.Count(m => requested.ModifierIds.Contains(m.Id));
                if (selectedInGroup < group.MinSelect || selectedInGroup > group.MaxSelect)
                {
                    return PricingResult.Fail(
                        $"'{group.Name}' on '{menuItem.Name}' requires between {group.MinSelect} and {group.MaxSelect} selections; got {selectedInGroup}.");
                }
            }

            var snapshot = new OrderLine(menuItem.Id, menuItem.Name, menuItem.BasePrice, requested.Quantity, lineModifiers);
            snapshots.Add(snapshot);
            total += snapshot.LineTotal;
        }

        return PricingResult.Ok(snapshots, total);
    }
}
