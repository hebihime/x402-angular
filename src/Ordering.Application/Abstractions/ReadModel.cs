using Ordering.Domain;
using Ordering.Domain.Orders;

namespace Ordering.Application.Abstractions;

// Read-side DTOs come straight from the denormalized projection tables via
// Dapper. Query handlers never touch the EF Core DbContext.

public sealed record RestaurantDto(Guid Id, string Name, string City);

public sealed record MenuModifierDto(Guid Id, string Name, Money PriceDelta);

public sealed record ModifierGroupDto(Guid Id, string Name, int MinSelect, int MaxSelect, IReadOnlyList<MenuModifierDto> Modifiers);

public sealed record MenuItemDto(Guid Id, string Name, Money BasePrice, IReadOnlyList<ModifierGroupDto> ModifierGroups);

public sealed record MenuDto(Guid RestaurantId, string RestaurantName, string City, IReadOnlyList<MenuItemDto> Items);

public sealed record OrderLineDto(
    Guid MenuItemId,
    string Name,
    Money UnitPrice,
    int Quantity,
    Money LineTotal,
    IReadOnlyList<OrderLineModifierDto> Modifiers);

public sealed record OrderLineModifierDto(Guid ModifierId, string Name, Money PriceDelta);

public sealed record OrderSummaryDto(
    Guid OrderId,
    Guid RestaurantId,
    string CustomerId,
    string Status,
    Money Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int RefundAttempts,
    string? LastRefundError,
    bool ManualInterventionRequired);

public sealed record HistoryEntryDto(string? From, string To, string Actor, DateTimeOffset At, string? Reason);

public sealed record OrderDetailsDto(
    Guid OrderId,
    Guid RestaurantId,
    string CustomerId,
    string Status,
    Money Total,
    IReadOnlyList<OrderLineDto> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    int RefundAttempts,
    string? LastRefundError,
    bool ManualInterventionRequired,
    IReadOnlyList<HistoryEntryDto> History);

public interface IRestaurantReadRepository
{
    Task<IReadOnlyList<RestaurantDto>> ListAsync(string? city, CancellationToken cancellationToken);

    Task<MenuDto?> GetMenuAsync(Guid restaurantId, CancellationToken cancellationToken);
}

public interface IOrderReadRepository
{
    Task<IReadOnlyList<OrderSummaryDto>> ListForRestaurantAsync(Guid restaurantId, OrderStatus? status, CancellationToken cancellationToken);

    Task<OrderDetailsDto?> GetAsync(Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HistoryEntryDto>> GetHistoryAsync(Guid orderId, CancellationToken cancellationToken);
}
