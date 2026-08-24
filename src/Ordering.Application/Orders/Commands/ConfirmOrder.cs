using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using Ordering.Application.Abstractions;
using Ordering.Application.Common;
using Ordering.Domain;
using Ordering.Domain.Guardrails;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.Commands;

/// <summary>
/// Second phase of two-phase ordering: charges the gateway and moves the draft
/// to paid. Replays settle nothing — the order id is the gateway idempotency
/// key, a unique constraint on the charge id backs it at the database level,
/// and an already-paid order short-circuits to its current state.
/// </summary>
public sealed record ConfirmOrderCommand(Guid OrderId, string CustomerId) : ICommand<Result<OrderDto>>;

public sealed class ConfirmOrderCommandValidator : AbstractValidator<ConfirmOrderCommand>
{
    public ConfirmOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.CustomerId).NotEmpty().MaximumLength(200);
    }
}

public sealed class ConfirmOrderCommandHandler(
    IOrderWriteRepository orders,
    IUnitOfWork unitOfWork,
    IPaymentGateway paymentGateway,
    IOptions<OrderingOptions> options,
    TimeProvider clock) : IRequestHandler<ConfirmOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(ConfirmOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetForUpdateAsync(command.OrderId, cancellationToken);
        if (order is null || order.CustomerId != command.CustomerId)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        if (order.Status != OrderStatus.Draft)
        {
            // Replayed or out-of-order confirm: no charge, no transition —
            // return the current state (the original success response for a
            // paid order).
            return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
        }

        var now = clock.GetUtcNow();
        if (now >= order.ExpiresAt)
        {
            order.TransitionTo(OrderStatus.Expired, Actor.System, now, "draft TTL elapsed at confirm");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<OrderDto>.Fail(ErrorKind.Conflict, "The draft has expired and can no longer be confirmed.");
        }

        var settings = options.Value;
        var guardrailError = await RecheckGuardrailsAsync(order, settings, now, cancellationToken);
        if (guardrailError is not null)
        {
            return guardrailError;
        }

        var charge = await paymentGateway.ChargeAsync(order.Id, order.CustomerId, order.Total, cancellationToken);
        if (!charge.Succeeded)
        {
            return Result<OrderDto>.Fail(ErrorKind.PaymentFailed, charge.Error ?? "The payment was declined.");
        }

        order.Confirm(charge.TransactionId!, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
    }

    private async Task<Result<OrderDto>?> RecheckGuardrailsAsync(
        Domain.Orders.Order order,
        OrderingOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var maxOrderValue = SpendGuardrails.CheckMaxOrderValue(order.Total, new Money(settings.MaxOrderValueMinorUnits));
        if (!maxOrderValue.Passed)
        {
            return Result<OrderDto>.Fail(ErrorKind.GuardrailViolation, maxOrderValue.Violation!);
        }

        var utcDayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var priorSpend = await orders.GetSpendSinceAsync(order.CustomerId, utcDayStart, order.Id, cancellationToken);
        var dailyCap = SpendGuardrails.CheckDailySpendCap(priorSpend, order.Total, new Money(settings.DailySpendCapMinorUnits));
        if (!dailyCap.Passed)
        {
            return Result<OrderDto>.Fail(ErrorKind.GuardrailViolation, dailyCap.Violation!);
        }

        return null;
    }
}
