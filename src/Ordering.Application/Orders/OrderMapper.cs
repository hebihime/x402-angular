using Ordering.Application.Abstractions;
using Ordering.Domain;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders;

/// <summary>Write-side command response: the order's current state, mapped by hand.</summary>
public sealed record OrderDto(
    Guid OrderId,
    Guid RestaurantId,
    string CustomerId,
    string Status,
    Money Total,
    IReadOnlyList<OrderLineDto> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int RefundAttempts,
    string? LastRefundError,
    bool ManualInterventionRequired);

public static class OrderMapper
{
    public static OrderDto ToDto(Order order) => new(
        order.Id,
        order.RestaurantId,
        order.CustomerId,
        order.Status.Name(),
        order.Total,
        order.Lines.Select(ToDto).ToArray(),
        order.CreatedAt,
        order.ExpiresAt,
        order.RefundAttempts,
        order.LastRefundError,
        order.ManualInterventionRequired);

    public static OrderLineDto ToDto(OrderLine line) => new(
        line.MenuItemId,
        line.Name,
        line.UnitPrice,
        line.Quantity,
        line.LineTotal,
        line.Modifiers.Select(m => new OrderLineModifierDto(m.ModifierId, m.Name, m.PriceDelta)).ToArray());
}
