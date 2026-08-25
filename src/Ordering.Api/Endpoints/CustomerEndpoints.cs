using MediatR;
using Ordering.Api.Http;
using Ordering.Application.Catalog.Queries;
using Ordering.Application.Orders.Commands;
using Ordering.Application.Orders.Queries;

namespace Ordering.Api.Endpoints;

// Customer surface. Endpoints send MediatR requests and map results — nothing
// else. The Customer actor is implied by these routes, never by payloads.

public sealed record PlaceOrderRequest(Guid RestaurantId, List<PlaceOrderRequestLine> Lines);

public sealed record PlaceOrderRequestLine(Guid MenuItemId, int Quantity, List<Guid>? ModifierIds);

public static class CustomerEndpoints
{
    public const string CustomerIdHeader = "X-Customer-Id";
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static void MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/restaurants", async (string? city, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListRestaurantsQuery(city), ct)));

        app.MapGet("/api/restaurants/{restaurantId:guid}/menu", async (Guid restaurantId, ISender sender, CancellationToken ct) =>
        {
            var menu = await sender.Send(new GetMenuQuery(restaurantId), ct);
            return menu is null ? Results.Problem(title: "Not found", detail: "Restaurant not found.", statusCode: 404) : Results.Ok(menu);
        });

        app.MapPost("/api/orders", async (PlaceOrderRequest request, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            if (!TryGetHeader(http, CustomerIdHeader, out var customerId, out var problem)
                || !TryGetHeader(http, IdempotencyKeyHeader, out var idempotencyKey, out problem))
            {
                return problem;
            }

            var lines = request.Lines
                .Select(l => new PlaceOrderLine(l.MenuItemId, l.Quantity, l.ModifierIds ?? []))
                .ToArray();
            var result = await sender.Send(new PlaceOrderCommand(request.RestaurantId, customerId, idempotencyKey, lines), ct);
            return result.ToHttpResult(StatusCodes.Status201Created);
        });

        app.MapPost("/api/orders/{orderId:guid}/confirm", async (Guid orderId, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            if (!TryGetHeader(http, CustomerIdHeader, out var customerId, out var problem))
            {
                return problem;
            }

            var paymentHeader = http.Request.Headers["X-PAYMENT"].ToString();
            var resource = $"{http.Request.Scheme}://{http.Request.Host}/api/orders/{orderId}/confirm";
            var result = await sender.Send(
                new ConfirmOrderCommand(orderId, customerId, string.IsNullOrWhiteSpace(paymentHeader) ? null : paymentHeader, resource),
                ct);
            return result.ToHttpResult();
        });

        app.MapPost("/api/orders/{orderId:guid}/cancel", async (Guid orderId, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            if (!TryGetHeader(http, CustomerIdHeader, out var customerId, out var problem))
            {
                return problem;
            }

            var result = await sender.Send(new CancelOrderCommand(orderId, customerId), ct);
            return result.ToHttpResult();
        });

        app.MapGet("/api/orders/{orderId:guid}", async (Guid orderId, ISender sender, CancellationToken ct) =>
        {
            var order = await sender.Send(new GetOrderQuery(orderId), ct);
            return order is null
                ? Results.Problem(title: "Not found", detail: "Order not found (or not yet projected).", statusCode: 404)
                : Results.Ok(order);
        });

        app.MapGet("/api/orders/{orderId:guid}/history", async (Guid orderId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetOrderHistoryQuery(orderId), ct)));
    }

    internal static bool TryGetHeader(HttpContext http, string name, out string value, out IResult problem)
    {
        var raw = http.Request.Headers[name].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = string.Empty;
            problem = Results.Problem(title: "Missing header", detail: $"The {name} header is required.", statusCode: 400);
            return false;
        }

        value = raw;
        problem = Results.Empty;
        return true;
    }
}
