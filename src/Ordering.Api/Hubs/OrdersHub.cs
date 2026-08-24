using Microsoft.AspNetCore.SignalR;
using Ordering.Application.Abstractions;

namespace Ordering.Api.Hubs;

/// <summary>
/// Live dashboard updates. Clients join a per-restaurant group and receive
/// every projected order event for it; on reconnect they refetch the board.
/// </summary>
public sealed class OrdersHub : Hub
{
    public static string RestaurantGroup(Guid restaurantId) => $"restaurant-{restaurantId}";

    public Task JoinRestaurant(Guid restaurantId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));

    public Task LeaveRestaurant(Guid restaurantId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));
}

/// <summary>Bridges the projector (infrastructure) to SignalR (API concern).</summary>
public sealed class SignalRProjectionNotifier(IHubContext<OrdersHub> hubContext) : IOrderProjectionNotifier
{
    public Task PublishAsync(OrderProjectionEvent projectionEvent, CancellationToken cancellationToken) =>
        hubContext.Clients
            .Group(OrdersHub.RestaurantGroup(projectionEvent.Order.RestaurantId))
            .SendAsync("orderProjected", projectionEvent, cancellationToken);
}
