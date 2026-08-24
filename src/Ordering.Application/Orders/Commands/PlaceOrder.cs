using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using Ordering.Application.Abstractions;
using Ordering.Application.Common;
using Ordering.Domain;
using Ordering.Domain.Guardrails;
using Ordering.Domain.Orders;
using Ordering.Domain.Pricing;

namespace Ordering.Application.Orders.Commands;

/// <summary>
/// Creates a draft. Note the request shape: item ids, quantities, and modifier
/// ids only — there is nowhere to put a client-supplied price. The server
/// reprices from the current menu, snapshots the lines, locks the total, and
/// sets the draft expiry. Placing is free; payment happens at confirm.
/// </summary>
public sealed record PlaceOrderCommand(
    Guid RestaurantId,
    string CustomerId,
    string IdempotencyKey,
    IReadOnlyList<PlaceOrderLine> Lines) : IIdempotentCommand<Result<OrderDto>>;

public sealed record PlaceOrderLine(Guid MenuItemId, int Quantity, IReadOnlyList<Guid> ModifierIds);

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEmpty();
        RuleFor(c => c.CustomerId).NotEmpty().MaximumLength(200);
        RuleFor(c => c.IdempotencyKey).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Lines).NotEmpty();
        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MenuItemId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 50);
        });
    }
}

public sealed class PlaceOrderCommandHandler(
    IOrderWriteRepository orders,
    IUnitOfWork unitOfWork,
    IOptions<OrderingOptions> options,
    TimeProvider clock) : IRequestHandler<PlaceOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await orders.GetRestaurantWithMenuAsync(command.RestaurantId, cancellationToken);
        if (restaurant is null)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Restaurant not found.");
        }

        var requested = command.Lines
            .Select(l => new RequestedLine(l.MenuItemId, l.Quantity, l.ModifierIds))
            .ToArray();

        var pricing = OrderPricer.Price(restaurant, requested);
        if (!pricing.Success)
        {
            return Result<OrderDto>.Fail(ErrorKind.Validation, pricing.Error!);
        }

        var settings = options.Value;
        var now = clock.GetUtcNow();

        var guardrail = await CheckGuardrailsAsync(command.CustomerId, pricing.Total, excludeOrderId: null, settings, now, cancellationToken);
        if (guardrail is not null)
        {
            return guardrail;
        }

        var order = Order.Place(
            Guid.NewGuid(),
            restaurant.Id,
            command.CustomerId,
            command.IdempotencyKey,
            pricing.Lines,
            pricing.Total,
            now,
            TimeSpan.FromSeconds(settings.DraftTtlSeconds));

        orders.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
    }

    private async Task<Result<OrderDto>?> CheckGuardrailsAsync(
        string customerId,
        Money total,
        Guid? excludeOrderId,
        OrderingOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var maxOrderValue = SpendGuardrails.CheckMaxOrderValue(total, new Money(settings.MaxOrderValueMinorUnits));
        if (!maxOrderValue.Passed)
        {
            return Result<OrderDto>.Fail(ErrorKind.GuardrailViolation, maxOrderValue.Violation!);
        }

        var utcDayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var priorSpendToday = await orders.GetSpendSinceAsync(customerId, utcDayStart, excludeOrderId, cancellationToken);
        var dailyCap = SpendGuardrails.CheckDailySpendCap(priorSpendToday, total, new Money(settings.DailySpendCapMinorUnits));
        if (!dailyCap.Passed)
        {
            return Result<OrderDto>.Fail(ErrorKind.GuardrailViolation, dailyCap.Violation!);
        }

        return null;
    }
}

/// <summary>Replays the draft an earlier PlaceOrder with the same key already created.</summary>
public sealed class PlaceOrderReplayer(IOrderWriteRepository orders)
    : IIdempotencyReplayer<PlaceOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>?> FindExistingAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var existing = await orders.FindByIdempotencyKeyAsync(command.CustomerId, command.IdempotencyKey, cancellationToken);
        return existing is null ? null : Result<OrderDto>.Ok(OrderMapper.ToDto(existing));
    }
}
