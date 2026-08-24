using System.Text.Json;
using Dapper;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Infrastructure.ReadModel;

public sealed class RestaurantReadRepository(NpgsqlDataSource dataSource) : IRestaurantReadRepository
{
    public async Task<IReadOnlyList<RestaurantDto>> ListAsync(string? city, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<(Guid Id, string Name, string City)>(new CommandDefinition(
            """
            SELECT id, name, city FROM read_restaurants
            WHERE @city::text IS NULL OR city = @city
            ORDER BY name
            """,
            new { city },
            cancellationToken: cancellationToken));
        return rows.Select(r => new RestaurantDto(r.Id, r.Name, r.City)).ToArray();
    }

    public async Task<MenuDto?> GetMenuAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var json = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT menu FROM read_menus WHERE restaurant_id = @restaurantId",
            new { restaurantId },
            cancellationToken: cancellationToken));
        return json is null ? null : JsonSerializer.Deserialize<MenuDto>(json, OrderingJson.Options);
    }
}
