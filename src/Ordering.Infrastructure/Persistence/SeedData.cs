using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Domain;
using Ordering.Domain.Catalog;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// Deterministic demo catalog: fixed ids so the demo script and docs can refer
/// to them. Seeded once on startup if the catalog is empty.
/// </summary>
public static class SeedData
{
    public static readonly Guid PixelPizzaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid MargheritaId = Guid.Parse("11111111-1111-1111-1111-aaaaaaaaaaa1");
    public static readonly Guid DiavolaId = Guid.Parse("11111111-1111-1111-1111-aaaaaaaaaaa2");
    public static readonly Guid PizzaSizeGroupId = Guid.Parse("11111111-1111-1111-1111-bbbbbbbbbbb1");
    public static readonly Guid PizzaSizeSmallId = Guid.Parse("11111111-1111-1111-1111-ccccccccccc1");
    public static readonly Guid PizzaSizeLargeId = Guid.Parse("11111111-1111-1111-1111-ccccccccccc2");
    public static readonly Guid PizzaToppingsGroupId = Guid.Parse("11111111-1111-1111-1111-bbbbbbbbbbb2");
    public static readonly Guid ToppingMushroomsId = Guid.Parse("11111111-1111-1111-1111-ccccccccccc3");
    public static readonly Guid ToppingOlivesId = Guid.Parse("11111111-1111-1111-1111-ccccccccccc4");
    public static readonly Guid ToppingExtraCheeseId = Guid.Parse("11111111-1111-1111-1111-ccccccccccc5");

    public static readonly Guid NoodleNexusId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid PadThaiId = Guid.Parse("22222222-2222-2222-2222-aaaaaaaaaaa1");
    public static readonly Guid ProteinGroupId = Guid.Parse("22222222-2222-2222-2222-bbbbbbbbbbb1");
    public static readonly Guid ProteinTofuId = Guid.Parse("22222222-2222-2222-2222-ccccccccccc1");
    public static readonly Guid ProteinChickenId = Guid.Parse("22222222-2222-2222-2222-ccccccccccc2");
    public static readonly Guid ProteinShrimpId = Guid.Parse("22222222-2222-2222-2222-ccccccccccc3");
    public static readonly Guid SpiceGroupId = Guid.Parse("22222222-2222-2222-2222-bbbbbbbbbbb2");
    public static readonly Guid SpiceMildId = Guid.Parse("22222222-2222-2222-2222-ccccccccccc4");
    public static readonly Guid SpiceHotId = Guid.Parse("22222222-2222-2222-2222-ccccccccccc5");

    public static readonly Guid BurgerBureauId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ClassicBurgerId = Guid.Parse("33333333-3333-3333-3333-aaaaaaaaaaa1");
    public static readonly Guid FriesId = Guid.Parse("33333333-3333-3333-3333-aaaaaaaaaaa2");
    public static readonly Guid AddOnsGroupId = Guid.Parse("33333333-3333-3333-3333-bbbbbbbbbbb1");
    public static readonly Guid AddOnBaconId = Guid.Parse("33333333-3333-3333-3333-ccccccccccc1");
    public static readonly Guid AddOnAvocadoId = Guid.Parse("33333333-3333-3333-3333-ccccccccccc2");

    public static async Task SeedAsync(OrderingDbContext dbContext, NpgsqlDataSource dataSource, ILogger logger, CancellationToken cancellationToken)
    {
        if (!await dbContext.Restaurants.AnyAsync(cancellationToken))
        {
            dbContext.Restaurants.AddRange(BuildRestaurants());
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded demo restaurants and menus");
        }

        await SeedReadCatalogAsync(dbContext, dataSource, cancellationToken);
    }

    /// <summary>
    /// The catalog is immutable reference data with no lifecycle, so it is
    /// seeded straight into the read tables instead of being projected.
    /// Upserts make this idempotent across restarts.
    /// </summary>
    private static async Task SeedReadCatalogAsync(OrderingDbContext dbContext, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var restaurants = await dbContext.Restaurants
            .AsNoTracking()
            .Include(r => r.MenuItems)
            .ThenInclude(mi => mi.ModifierGroups)
            .ThenInclude(g => g.Modifiers)
            .ToListAsync(cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var restaurant in restaurants)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO read_restaurants (id, name, city) VALUES (@Id, @Name, @City)
                ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, city = EXCLUDED.city
                """,
                new { restaurant.Id, restaurant.Name, restaurant.City },
                cancellationToken: cancellationToken));

            var menu = new MenuDto(
                restaurant.Id,
                restaurant.Name,
                restaurant.City,
                restaurant.MenuItems
                    .OrderBy(mi => mi.Name)
                    .Select(mi => new MenuItemDto(
                        mi.Id,
                        mi.Name,
                        mi.BasePrice,
                        mi.ModifierGroups
                            .OrderBy(g => g.Name)
                            .Select(g => new ModifierGroupDto(
                                g.Id,
                                g.Name,
                                g.MinSelect,
                                g.MaxSelect,
                                g.Modifiers.OrderBy(m => m.Name).Select(m => new MenuModifierDto(m.Id, m.Name, m.PriceDelta)).ToArray()))
                            .ToArray()))
                    .ToArray());

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO read_menus (restaurant_id, menu) VALUES (@RestaurantId, @Menu::jsonb)
                ON CONFLICT (restaurant_id) DO UPDATE SET menu = EXCLUDED.menu
                """,
                new { RestaurantId = restaurant.Id, Menu = JsonSerializer.Serialize(menu, OrderingJson.Options) },
                cancellationToken: cancellationToken));
        }
    }

    public static List<Restaurant> BuildRestaurants()
    {
        var pixelPizza = new Restaurant(PixelPizzaId, "Pixel Pizza", "Bangkok");
        var margherita = new MenuItem(MargheritaId, PixelPizzaId, "Margherita", new Money(1195));
        var sizeGroup = new ModifierGroup(PizzaSizeGroupId, MargheritaId, "Size", minSelect: 1, maxSelect: 1);
        sizeGroup.Modifiers.Add(new Modifier(PizzaSizeSmallId, PizzaSizeGroupId, "Small", new Money(0)));
        sizeGroup.Modifiers.Add(new Modifier(PizzaSizeLargeId, PizzaSizeGroupId, "Large", new Money(350)));
        var toppingsGroup = new ModifierGroup(PizzaToppingsGroupId, MargheritaId, "Extra toppings", minSelect: 0, maxSelect: 3);
        toppingsGroup.Modifiers.Add(new Modifier(ToppingMushroomsId, PizzaToppingsGroupId, "Mushrooms", new Money(150)));
        toppingsGroup.Modifiers.Add(new Modifier(ToppingOlivesId, PizzaToppingsGroupId, "Olives", new Money(125)));
        toppingsGroup.Modifiers.Add(new Modifier(ToppingExtraCheeseId, PizzaToppingsGroupId, "Extra cheese", new Money(200)));
        margherita.ModifierGroups.Add(sizeGroup);
        margherita.ModifierGroups.Add(toppingsGroup);
        var diavola = new MenuItem(DiavolaId, PixelPizzaId, "Diavola", new Money(1450));
        pixelPizza.MenuItems.Add(margherita);
        pixelPizza.MenuItems.Add(diavola);

        var noodleNexus = new Restaurant(NoodleNexusId, "Noodle Nexus", "Bangkok");
        var padThai = new MenuItem(PadThaiId, NoodleNexusId, "Pad Thai", new Money(1050));
        var proteinGroup = new ModifierGroup(ProteinGroupId, PadThaiId, "Protein", minSelect: 1, maxSelect: 1);
        proteinGroup.Modifiers.Add(new Modifier(ProteinTofuId, ProteinGroupId, "Tofu", new Money(0)));
        proteinGroup.Modifiers.Add(new Modifier(ProteinChickenId, ProteinGroupId, "Chicken", new Money(150)));
        proteinGroup.Modifiers.Add(new Modifier(ProteinShrimpId, ProteinGroupId, "Shrimp", new Money(295)));
        var spiceGroup = new ModifierGroup(SpiceGroupId, PadThaiId, "Spice level", minSelect: 0, maxSelect: 1);
        spiceGroup.Modifiers.Add(new Modifier(SpiceMildId, SpiceGroupId, "Mild", new Money(0)));
        spiceGroup.Modifiers.Add(new Modifier(SpiceHotId, SpiceGroupId, "Thai hot", new Money(0)));
        padThai.ModifierGroups.Add(proteinGroup);
        padThai.ModifierGroups.Add(spiceGroup);
        noodleNexus.MenuItems.Add(padThai);

        var burgerBureau = new Restaurant(BurgerBureauId, "Burger Bureau", "Chiang Mai");
        var classicBurger = new MenuItem(ClassicBurgerId, BurgerBureauId, "Classic Burger", new Money(995));
        var addOnsGroup = new ModifierGroup(AddOnsGroupId, ClassicBurgerId, "Add-ons", minSelect: 0, maxSelect: 2);
        addOnsGroup.Modifiers.Add(new Modifier(AddOnBaconId, AddOnsGroupId, "Bacon", new Money(180)));
        addOnsGroup.Modifiers.Add(new Modifier(AddOnAvocadoId, AddOnsGroupId, "Avocado", new Money(160)));
        classicBurger.ModifierGroups.Add(addOnsGroup);
        var fries = new MenuItem(FriesId, BurgerBureauId, "Fries", new Money(350));
        burgerBureau.MenuItems.Add(classicBurger);
        burgerBureau.MenuItems.Add(fries);

        return [pixelPizza, noodleNexus, burgerBureau];
    }
}
