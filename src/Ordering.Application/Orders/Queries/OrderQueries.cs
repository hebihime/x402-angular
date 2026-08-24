using MediatR;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.Queries;

// Query handlers read ONLY the denormalized projection tables via Dapper.
// They are eventually consistent with the write side by design.

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDetailsDto?>;

public sealed record GetOrderHistoryQuery(Guid OrderId) : IQuery<IReadOnlyList<HistoryEntryDto>>;

public sealed record ListRestaurantOrdersQuery(Guid RestaurantId, OrderStatus? Status) : IQuery<IReadOnlyList<OrderSummaryDto>>;

internal sealed class GetOrderQueryHandler(IOrderReadRepository orders) : IRequestHandler<GetOrderQuery, OrderDetailsDto?>
{
    public Task<OrderDetailsDto?> Handle(GetOrderQuery query, CancellationToken cancellationToken) =>
        orders.GetAsync(query.OrderId, cancellationToken);
}

internal sealed class GetOrderHistoryQueryHandler(IOrderReadRepository orders)
    : IRequestHandler<GetOrderHistoryQuery, IReadOnlyList<HistoryEntryDto>>
{
    public Task<IReadOnlyList<HistoryEntryDto>> Handle(GetOrderHistoryQuery query, CancellationToken cancellationToken) =>
        orders.GetHistoryAsync(query.OrderId, cancellationToken);
}

internal sealed class ListRestaurantOrdersQueryHandler(IOrderReadRepository orders)
    : IRequestHandler<ListRestaurantOrdersQuery, IReadOnlyList<OrderSummaryDto>>
{
    public Task<IReadOnlyList<OrderSummaryDto>> Handle(ListRestaurantOrdersQuery query, CancellationToken cancellationToken) =>
        orders.ListForRestaurantAsync(query.RestaurantId, query.Status, cancellationToken);
}
