using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Ordering.Application.Abstractions;
using Ordering.Infrastructure.Workers;
using Testcontainers.PostgreSql;

namespace Ordering.Tests.Integration;

/// <summary>
/// One real Postgres (Testcontainers) and one API host for the whole
/// collection. Background polling loops are removed so tests drive the
/// projector/workers deterministically; the clock is a FakeTimeProvider; the
/// SignalR notifier is replaced with a capturing fake (the projector's
/// contract, not SignalR transport, is what the invariants cover).
/// </summary>
public sealed class OrderingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));

    public CapturingProjectionNotifier Notifier { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Ordering", _postgres.GetConnectionString());
        builder.UseSetting("Ordering:DraftTtlSeconds", "200000");
        builder.UseSetting("Ordering:AcceptanceTimeoutSeconds", "900");
        builder.UseSetting("Ordering:MaxOrderValueMinorUnits", "20000");
        builder.UseSetting("Ordering:DailySpendCapMinorUnits", "50000");
        builder.UseSetting("Ordering:Refund:MaxAttempts", "3");
        builder.UseSetting("Ordering:Refund:BackoffBaseMs", "2000");
        builder.UseSetting("Ordering:Refund:BackoffCapMs", "60000");

        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType is not null
                    && typeof(PollingBackgroundService).IsAssignableFrom(d.ImplementationType))
                .ToList())
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IOrderProjectionNotifier>();
            services.AddSingleton<IOrderProjectionNotifier>(Notifier);
        });
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed class CapturingProjectionNotifier : IOrderProjectionNotifier
{
    public ConcurrentQueue<OrderProjectionEvent> Events { get; } = new();

    public Task PublishAsync(OrderProjectionEvent projectionEvent, CancellationToken cancellationToken)
    {
        Events.Enqueue(projectionEvent);
        return Task.CompletedTask;
    }
}

[CollectionDefinition("integration")]
public sealed class IntegrationCollection : ICollectionFixture<OrderingApiFactory>;
