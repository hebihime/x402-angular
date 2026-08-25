using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ordering.Api.Endpoints;
using Ordering.Api.Http;
using Ordering.Api.Hubs;
using Ordering.Application;
using Ordering.Application.Abstractions;
using Ordering.Infrastructure;
using Ordering.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrderingApplication();
builder.Services.AddOrderingInfrastructure(builder.Configuration);
builder.Services.AddOrderingWorkers();
builder.Services.AddSingleton<IOrderProjectionNotifier, SignalRProjectionNotifier>();

builder.Services.AddOptions<OrderingOptions>()
    .BindConfiguration(OrderingOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => !string.IsNullOrWhiteSpace(o.X402.PayToAddress), "Ordering__X402__PayToAddress is required.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

builder.Services.AddSignalR();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();

app.MapCustomerEndpoints();
app.MapDashboardEndpoints();
app.MapDemoEndpoints();
app.MapHub<OrdersHub>("/hubs/orders");

// Integration tests own their database lifecycle and disable this.
if (!app.Configuration.GetValue<bool>("Ordering:SkipMigrations"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    await dbContext.Database.MigrateAsync();
    await SeedData.SeedAsync(dbContext, scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(), app.Logger, CancellationToken.None);
}

app.Run();

public partial class Program;
