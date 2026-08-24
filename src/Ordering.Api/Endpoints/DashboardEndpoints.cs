using MediatR;
using Ordering.Api.Http;
using Ordering.Application.Orders.Queries;
using Ordering.Application.Orders.Commands;
using Ordering.Domain.Orders;

namespace Ordering.Api.Endpoints;

// Restaurant/dashboard surface. The Restaurant actor is implied by these
// routes. Reads come from the projection; transitions are commands.

public sealed record RejectOrderRequest(string? Reason);

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/restaurants/{restaurantId:guid}/orders", async (Guid restaurantId, string? status, ISender sender, CancellationToken ct) =>
        {
            OrderStatus? parsed = null;
            if (!string.IsNullOrEmpty(status))
            {
                if (!Wire.TryParseOrderStatus(status, out var value))
                {
                    return Results.Problem(title: "Invalid request", detail: $"Unknown status '{status}'.", statusCode: 400);
                }

                parsed = value;
            }

            return Results.Ok(await sender.Send(new ListRestaurantOrdersQuery(restaurantId, parsed), ct));
        });

        app.MapPost("/api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/accept",
            async (Guid restaurantId, Guid orderId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new AcceptOrderCommand(restaurantId, orderId), ct)).ToHttpResult());

        app.MapPost("/api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/reject",
            async (Guid restaurantId, Guid orderId, RejectOrderRequest? request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RejectOrderCommand(restaurantId, orderId, request?.Reason), ct)).ToHttpResult());

        app.MapPost("/api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/start-preparing",
            async (Guid restaurantId, Guid orderId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new StartPreparingCommand(restaurantId, orderId), ct)).ToHttpResult());

        app.MapPost("/api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/mark-ready",
            async (Guid restaurantId, Guid orderId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new MarkReadyCommand(restaurantId, orderId), ct)).ToHttpResult());

        app.MapPost("/api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/complete",
            async (Guid restaurantId, Guid orderId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CompleteOrderCommand(restaurantId, orderId), ct)).ToHttpResult());
    }
}
