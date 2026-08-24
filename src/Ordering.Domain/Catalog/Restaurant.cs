namespace Ordering.Domain.Catalog;

/// <summary>
/// Catalog reference data. Location is a city string with equality filtering
/// only — no geo. Modifiers are exactly one level deep: MenuItem →
/// ModifierGroup (min/max select) → Modifier (price delta), never recursive.
/// </summary>
public sealed class Restaurant
{
    private Restaurant()
    {
        Name = null!;
        City = null!;
    }

    public Restaurant(Guid id, string name, string city)
    {
        Id = id;
        Name = name;
        City = city;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string City { get; private set; }
    public List<MenuItem> MenuItems { get; private set; } = [];
}

public sealed class MenuItem
{
    private MenuItem()
    {
        Name = null!;
    }

    public MenuItem(Guid id, Guid restaurantId, string name, Money basePrice)
    {
        Id = id;
        RestaurantId = restaurantId;
        Name = name;
        BasePrice = basePrice;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public string Name { get; private set; }
    public Money BasePrice { get; private set; }
    public List<ModifierGroup> ModifierGroups { get; private set; } = [];
}

public sealed class ModifierGroup
{
    private ModifierGroup()
    {
        Name = null!;
    }

    public ModifierGroup(Guid id, Guid menuItemId, string name, int minSelect, int maxSelect)
    {
        Id = id;
        MenuItemId = menuItemId;
        Name = name;
        MinSelect = minSelect;
        MaxSelect = maxSelect;
    }

    public Guid Id { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string Name { get; private set; }
    public int MinSelect { get; private set; }
    public int MaxSelect { get; private set; }
    public List<Modifier> Modifiers { get; private set; } = [];
}

public sealed class Modifier
{
    private Modifier()
    {
        Name = null!;
    }

    public Modifier(Guid id, Guid modifierGroupId, string name, Money priceDelta)
    {
        Id = id;
        ModifierGroupId = modifierGroupId;
        Name = name;
        PriceDelta = priceDelta;
    }

    public Guid Id { get; private set; }
    public Guid ModifierGroupId { get; private set; }
    public string Name { get; private set; }
    public Money PriceDelta { get; private set; }
}
