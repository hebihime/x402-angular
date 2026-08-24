namespace Ordering.Domain.Orders;

/// <summary>
/// A snapshotted order line: name and prices are copied from the menu at
/// placement time and never change afterwards, even if the menu does.
/// </summary>
public sealed record OrderLine(
    Guid MenuItemId,
    string Name,
    Money UnitPrice,
    int Quantity,
    IReadOnlyList<OrderLineModifier> Modifiers)
{
    public Money LineTotal
    {
        get
        {
            var unit = UnitPrice;
            foreach (var modifier in Modifiers)
            {
                unit += modifier.PriceDelta;
            }

            return unit * Quantity;
        }
    }
}

public sealed record OrderLineModifier(Guid ModifierId, string Name, Money PriceDelta);
