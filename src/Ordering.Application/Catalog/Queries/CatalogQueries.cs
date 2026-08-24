using MediatR;
using Ordering.Application.Abstractions;

namespace Ordering.Application.Catalog.Queries;

public sealed record ListRestaurantsQuery(string? City) : IQuery<IReadOnlyList<RestaurantDto>>;

public sealed record GetMenuQuery(Guid RestaurantId) : IQuery<MenuDto?>;

internal sealed class ListRestaurantsQueryHandler(IRestaurantReadRepository restaurants)
    : IRequestHandler<ListRestaurantsQuery, IReadOnlyList<RestaurantDto>>
{
    public Task<IReadOnlyList<RestaurantDto>> Handle(ListRestaurantsQuery query, CancellationToken cancellationToken) =>
        restaurants.ListAsync(query.City, cancellationToken);
}

internal sealed class GetMenuQueryHandler(IRestaurantReadRepository restaurants)
    : IRequestHandler<GetMenuQuery, MenuDto?>
{
    public Task<MenuDto?> Handle(GetMenuQuery query, CancellationToken cancellationToken) =>
        restaurants.GetMenuAsync(query.RestaurantId, cancellationToken);
}
