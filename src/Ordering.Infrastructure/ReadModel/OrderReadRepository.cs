using System.Text.Json;
using Dapper;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Domain;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Infrastructure.ReadModel;

public sealed class OrderReadRepository(NpgsqlDataSource dataSource) : IOrderReadRepository
{
    // Dapper materializes plain rows (Npgsql surfaces timestamptz as UTC
    // DateTime); Money, DateTimeOffset, and DTO records are mapped by hand.
    private sealed record OrderRow(
        Guid OrderId,
        Guid RestaurantId,
        string CustomerId,
        string Status,
        long Total,
        string? Lines,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime ExpiresAt,
        int RefundAttempts,
        string? LastRefundError,
        bool ManualIntervention);

    private sealed record HistoryRow(string? FromStatus, string ToStatus, string Actor, DateTime At, string? Reason);

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private const string SummaryColumns =
        """
        order_id AS OrderId, restaurant_id AS RestaurantId, customer_id AS CustomerId,
        status AS Status, total AS Total, NULL::text AS Lines, created_at AS CreatedAt,
        updated_at AS UpdatedAt, expires_at AS ExpiresAt, refund_attempts AS RefundAttempts,
        last_refund_error AS LastRefundError, manual_intervention AS ManualIntervention
        """;

    public async Task<IReadOnlyList<OrderSummaryDto>> ListForRestaurantAsync(Guid restaurantId, OrderStatus? status, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(
            $"""
            SELECT {SummaryColumns}
            FROM read_orders
            WHERE restaurant_id = @restaurantId AND (@status::text IS NULL OR status = @status)
            ORDER BY created_at
            """,
            new { restaurantId, status = status?.Name() },
            cancellationToken: cancellationToken));
        return rows.Select(ToSummary).ToArray();
    }

    public async Task<OrderDetailsDto?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<OrderRow?>(new CommandDefinition(
            """
            SELECT order_id AS OrderId, restaurant_id AS RestaurantId, customer_id AS CustomerId,
                   status AS Status, total AS Total, lines AS Lines, created_at AS CreatedAt,
                   updated_at AS UpdatedAt, expires_at AS ExpiresAt, refund_attempts AS RefundAttempts,
                   last_refund_error AS LastRefundError, manual_intervention AS ManualIntervention
            FROM read_orders WHERE order_id = @orderId
            """,
            new { orderId },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var history = await QueryHistoryAsync(connection, orderId, cancellationToken);
        var lines = row.Lines is null
            ? []
            : JsonSerializer.Deserialize<List<OrderLineDto>>(row.Lines, OrderingJson.Options)!;

        return new OrderDetailsDto(
            row.OrderId,
            row.RestaurantId,
            row.CustomerId,
            row.Status,
            new Money(row.Total),
            lines,
            AsUtc(row.CreatedAt),
            AsUtc(row.UpdatedAt),
            AsUtc(row.ExpiresAt),
            row.RefundAttempts,
            row.LastRefundError,
            row.ManualIntervention,
            history);
    }

    public async Task<IReadOnlyList<HistoryEntryDto>> GetHistoryAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await QueryHistoryAsync(connection, orderId, cancellationToken);
    }

    private static async Task<IReadOnlyList<HistoryEntryDto>> QueryHistoryAsync(NpgsqlConnection connection, Guid orderId, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            """
            SELECT from_status AS FromStatus, to_status AS ToStatus, actor AS Actor, at AS At, reason AS Reason
            FROM read_order_history WHERE order_id = @orderId ORDER BY id
            """,
            new { orderId },
            cancellationToken: cancellationToken));
        return rows.Select(h => new HistoryEntryDto(h.FromStatus, h.ToStatus, h.Actor, AsUtc(h.At), h.Reason)).ToArray();
    }

    private static OrderSummaryDto ToSummary(OrderRow row) => new(
        row.OrderId,
        row.RestaurantId,
        row.CustomerId,
        row.Status,
        new Money(row.Total),
        AsUtc(row.CreatedAt),
        AsUtc(row.UpdatedAt),
        row.RefundAttempts,
        row.LastRefundError,
        row.ManualIntervention);
}
