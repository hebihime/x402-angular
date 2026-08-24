using FluentAssertions;
using Ordering.Domain;
using Ordering.Domain.Catalog;
using Ordering.Domain.Pricing;

namespace Ordering.Tests.Unit;

public class OrderPricerTests
{
    private static readonly Guid RestaurantId = Guid.NewGuid();
    private static readonly Guid PizzaId = Guid.NewGuid();
    private static readonly Guid SizeGroupId = Guid.NewGuid();
    private static readonly Guid SmallId = Guid.NewGuid();
    private static readonly Guid LargeId = Guid.NewGuid();
    private static readonly Guid ToppingsGroupId = Guid.NewGuid();
    private static readonly Guid CheeseId = Guid.NewGuid();
    private static readonly Guid OlivesId = Guid.NewGuid();
    private static readonly Guid FriesId = Guid.NewGuid();

    private static Restaurant BuildRestaurant()
    {
        var restaurant = new Restaurant(RestaurantId, "Test Pizza", "Bangkok");
        var pizza = new MenuItem(PizzaId, RestaurantId, "Pizza", new Money(1000));
        var size = new ModifierGroup(SizeGroupId, PizzaId, "Size", minSelect: 1, maxSelect: 1);
        size.Modifiers.Add(new Modifier(SmallId, SizeGroupId, "Small", new Money(0)));
        size.Modifiers.Add(new Modifier(LargeId, SizeGroupId, "Large", new Money(300)));
        var toppings = new ModifierGroup(ToppingsGroupId, PizzaId, "Toppings", minSelect: 0, maxSelect: 2);
        toppings.Modifiers.Add(new Modifier(CheeseId, ToppingsGroupId, "Cheese", new Money(200)));
        toppings.Modifiers.Add(new Modifier(OlivesId, ToppingsGroupId, "Olives", new Money(100)));
        pizza.ModifierGroups.Add(size);
        pizza.ModifierGroups.Add(toppings);
        restaurant.MenuItems.Add(pizza);
        restaurant.MenuItems.Add(new MenuItem(FriesId, RestaurantId, "Fries", new Money(350)));
        return restaurant;
    }

    [Fact]
    public void Reprices_from_the_menu_and_applies_modifier_deltas()
    {
        var result = OrderPricer.Price(BuildRestaurant(),
        [
            new RequestedLine(PizzaId, 2, [LargeId, CheeseId]),
            new RequestedLine(FriesId, 1, []),
        ]);

        result.Success.Should().BeTrue();
        result.Lines.Should().HaveCount(2);
        // (1000 + 300 + 200) * 2 = 3000, snapshot names and deltas copied.
        result.Lines[0].LineTotal.Should().Be(new Money(3000));
        result.Lines[0].Name.Should().Be("Pizza");
        result.Lines[0].UnitPrice.Should().Be(new Money(1000));
        result.Lines[0].Modifiers.Select(m => m.Name).Should().BeEquivalentTo(["Large", "Cheese"]);
        result.Total.Should().Be(new Money(3350));
    }

    [Fact]
    public void Rejects_an_empty_order()
    {
        OrderPricer.Price(BuildRestaurant(), []).Success.Should().BeFalse();
    }

    [Fact]
    public void Rejects_quantities_below_one()
    {
        var result = OrderPricer.Price(BuildRestaurant(), [new RequestedLine(FriesId, 0, [])]);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Quantity");
    }

    [Fact]
    public void Rejects_items_not_on_the_menu()
    {
        var result = OrderPricer.Price(BuildRestaurant(), [new RequestedLine(Guid.NewGuid(), 1, [])]);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
    }

    [Fact]
    public void Rejects_modifiers_that_belong_to_another_item()
    {
        var result = OrderPricer.Price(BuildRestaurant(), [new RequestedLine(FriesId, 1, [CheeseId])]);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not belong");
    }

    [Fact]
    public void Rejects_duplicate_modifiers()
    {
        var result = OrderPricer.Price(BuildRestaurant(),
            [new RequestedLine(PizzaId, 1, [SmallId, SmallId])]);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Duplicate");
    }

    [Fact]
    public void Enforces_group_minimum_selections()
    {
        // Size requires exactly one selection.
        var result = OrderPricer.Price(BuildRestaurant(), [new RequestedLine(PizzaId, 1, [CheeseId])]);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Size");
    }

    [Fact]
    public void Enforces_group_maximum_selections()
    {
        var result = OrderPricer.Price(BuildRestaurant(),
            [new RequestedLine(PizzaId, 1, [SmallId, LargeId])]);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Size");
    }
}
