using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ordering.Application.Abstractions;
using Ordering.Domain;
using Ordering.Domain.Catalog;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Outbox;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// The write model. SaveChanges drains every pending domain event from tracked
/// orders into outbox rows, so "status update + history row + outbox row in
/// one transaction" is enforced by the persistence layer itself — no handler
/// can emit an event anywhere else.
/// </summary>
public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<StatusHistoryEntry> StatusHistory => Set<StatusHistoryEntry>();
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CollectDomainEventsIntoOutbox();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void CollectDomainEventsIntoOutbox()
    {
        foreach (var entry in ChangeTracker.Entries<Order>().ToList())
        {
            foreach (var domainEvent in entry.Entity.DequeuePendingEvents())
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    OrderId = domainEvent.OrderId,
                    Type = domainEvent.GetType().Name,
                    Payload = JsonSerializer.Serialize<object>(domainEvent, OrderingJson.Options),
                    OccurredAt = domainEvent.OccurredAt,
                });
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var money = new ValueConverter<Money, long>(m => m.MinorUnits, v => new Money(v));
        var status = new ValueConverter<OrderStatus, string>(s => s.Name(), s => Wire.ParseOrderStatus(s));
        var actor = new ValueConverter<Actor, string>(a => a.Name(), a => Wire.ParseActor(a));

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.CustomerId).HasMaxLength(200);
            entity.Property(o => o.IdempotencyKey).HasMaxLength(200);
            entity.Property(o => o.Status).HasConversion(status).HasMaxLength(32);
            entity.Property(o => o.Total).HasConversion(money);
            entity.Property(o => o.ChargeId).HasMaxLength(100);
            entity.Property(o => o.RefundId).HasMaxLength(100);
            entity.Property(o => o.LastRefundError).HasMaxLength(1000);

            entity.Property(o => o.Lines)
                .HasConversion(
                    lines => JsonSerializer.Serialize(lines, OrderingJson.Options),
                    json => JsonSerializer.Deserialize<List<OrderLine>>(json, OrderingJson.Options)!,
                    new ValueComparer<IReadOnlyList<OrderLine>>(
                        (left, right) => ReferenceEquals(left, right) || (left != null && right != null && left.SequenceEqual(right)),
                        lines => lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line)),
                        lines => lines.ToList()))
                .HasField("_lines")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasColumnType("jsonb");

            entity.HasMany(o => o.History)
                .WithOne()
                .HasForeignKey(h => h.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(o => o.History)
                .HasField("_history")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            // Invariant 5: idempotent PlaceOrder and settle-once Confirm are
            // database guarantees, not application logic.
            entity.HasIndex(o => new { o.CustomerId, o.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("ux_orders_customer_idempotency_key");
            entity.HasIndex(o => o.ChargeId)
                .IsUnique()
                .HasDatabaseName("ux_orders_charge_id");

            // Worker scans and the daily-spend guardrail.
            entity.HasIndex(o => new { o.Status, o.ExpiresAt });
            entity.HasIndex(o => new { o.Status, o.UpdatedAt });
            entity.HasIndex(o => new { o.Status, o.NextRefundAttemptAt });
            entity.HasIndex(o => new { o.CustomerId, o.CreatedAt });
        });

        modelBuilder.Entity<StatusHistoryEntry>(entity =>
        {
            entity.ToTable("status_history");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.From).HasConversion(status).HasMaxLength(32);
            entity.Property(h => h.To).HasConversion(status).HasMaxLength(32);
            entity.Property(h => h.Actor).HasConversion(actor).HasMaxLength(16);
            entity.Property(h => h.Reason).HasMaxLength(1000);
            entity.HasIndex(h => h.OrderId);
        });

        modelBuilder.Entity<Restaurant>(entity =>
        {
            entity.ToTable("restaurants");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Name).HasMaxLength(200);
            entity.Property(r => r.City).HasMaxLength(100);
            entity.HasMany(r => r.MenuItems).WithOne().HasForeignKey(mi => mi.RestaurantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(r => r.City);
        });

        modelBuilder.Entity<MenuItem>(entity =>
        {
            entity.ToTable("menu_items");
            entity.HasKey(mi => mi.Id);
            entity.Property(mi => mi.Name).HasMaxLength(200);
            entity.Property(mi => mi.BasePrice).HasConversion(money);
            entity.HasMany(mi => mi.ModifierGroups).WithOne().HasForeignKey(g => g.MenuItemId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModifierGroup>(entity =>
        {
            entity.ToTable("modifier_groups");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Name).HasMaxLength(200);
            entity.HasMany(g => g.Modifiers).WithOne().HasForeignKey(m => m.ModifierGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Modifier>(entity =>
        {
            entity.ToTable("modifiers");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Name).HasMaxLength(200);
            entity.Property(m => m.PriceDelta).HasConversion(money);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Type).HasMaxLength(100);
            entity.Property(m => m.Payload).HasColumnType("jsonb");
            entity.HasIndex(m => m.ProcessedAt).HasFilter("processed_at IS NULL");
        });
    }
}
