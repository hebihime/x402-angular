using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Domain.Events;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Outbox;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Infrastructure.Projections;

/// <summary>
/// Drains the outbox in id order and applies each event to the denormalized
/// projection tables, marking rows processed in the same transaction. SignalR
/// notifications go out only after the commit, so the dashboard never hears
/// about a projection that didn't land. Single instance by design; ordering is
/// the whole guarantee.
/// </summary>
public sealed class OutboxProjectionProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProjectionProcessor> logger)
{
    public const int BatchSize = 100;

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IOrderProjectionNotifier>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
        {
            return 0;
        }

        var notifications = new List<OrderProjectionEvent>(messages.Count);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var connection = dbContext.Database.GetDbConnection();
            var dbTransaction = transaction.GetDbTransaction();

            foreach (var message in messages)
            {
                var notification = await ApplyAsync(connection, dbTransaction, message, cancellationToken);
                if (notification is not null)
                {
                    notifications.Add(notification);
                }

                message.ProcessedAt = clock.GetUtcNow();
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var notification in notifications)
        {
            try
            {
                await notifier.PublishAsync(notification, cancellationToken);
            }
            catch (Exception exception)
            {
                // A missed live update is not a projection failure; clients
                // refetch on reconnect.
                logger.LogWarning(exception, "Failed to broadcast projection event for order {OrderId}", notification.Order.OrderId);
            }
        }

        return messages.Count;
    }

    private async Task<OrderProjectionEvent?> ApplyAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        var (snapshot, historyDelta) = Deserialize(message);
        if (snapshot is null)
        {
            logger.LogError("Skipping unknown outbox message type {Type} (id {Id})", message.Type, message.Id);
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO read_orders (order_id, restaurant_id, customer_id, status, total, lines,
                                     created_at, updated_at, expires_at, refund_attempts,
                                     last_refund_error, manual_intervention)
            VALUES (@OrderId, @RestaurantId, @CustomerId, @Status, @Total, @Lines::jsonb,
                    @CreatedAt, @UpdatedAt, @ExpiresAt, @RefundAttempts,
                    @LastRefundError, @ManualIntervention)
            ON CONFLICT (order_id) DO UPDATE SET
                status = EXCLUDED.status,
                updated_at = EXCLUDED.updated_at,
                refund_attempts = EXCLUDED.refund_attempts,
                last_refund_error = EXCLUDED.last_refund_error,
                manual_intervention = EXCLUDED.manual_intervention
            """,
            new
            {
                snapshot.OrderId,
                snapshot.RestaurantId,
                snapshot.CustomerId,
                Status = snapshot.Status.Name(),
                Total = snapshot.Total.MinorUnits,
                Lines = JsonSerializer.Serialize(snapshot.Lines, OrderingJson.Options),
                snapshot.CreatedAt,
                UpdatedAt = message.OccurredAt,
                snapshot.ExpiresAt,
                snapshot.RefundAttempts,
                snapshot.LastRefundError,
                ManualIntervention = snapshot.ManualInterventionRequired,
            },
            transaction,
            cancellationToken: cancellationToken));

        HistoryEntryDto? historyDto = null;
        if (historyDelta is not null)
        {
            historyDto = new HistoryEntryDto(
                historyDelta.From?.Name(),
                historyDelta.To.Name(),
                historyDelta.Actor.Name(),
                historyDelta.At,
                historyDelta.Reason);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO read_order_history (order_id, from_status, to_status, actor, at, reason)
                VALUES (@OrderId, @FromStatus, @ToStatus, @Actor, @At, @Reason)
                """,
                new
                {
                    snapshot.OrderId,
                    FromStatus = historyDto.From,
                    ToStatus = historyDto.To,
                    historyDto.Actor,
                    historyDto.At,
                    historyDto.Reason,
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        var summary = new OrderSummaryDto(
            snapshot.OrderId,
            snapshot.RestaurantId,
            snapshot.CustomerId,
            snapshot.Status.Name(),
            snapshot.Total,
            snapshot.CreatedAt,
            message.OccurredAt,
            snapshot.RefundAttempts,
            snapshot.LastRefundError,
            snapshot.ManualInterventionRequired);

        return new OrderProjectionEvent(message.Type, summary, historyDto);
    }

    private static (OrderSnapshot? Snapshot, HistoryDelta? History) Deserialize(OutboxMessage message) =>
        message.Type switch
        {
            nameof(OrderPlaced) when Deserialize<OrderPlaced>(message) is { } e => (e.Order, e.History),
            nameof(OrderStatusChanged) when Deserialize<OrderStatusChanged>(message) is { } e => (e.Order, e.History),
            nameof(RefundAttemptFailed) when Deserialize<RefundAttemptFailed>(message) is { } e => (e.Order, null),
            _ => (null, null),
        };

    private static T? Deserialize<T>(OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, OrderingJson.Options);
}
