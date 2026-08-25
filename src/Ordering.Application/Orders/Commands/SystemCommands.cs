using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using Ordering.Application.Abstractions;
using Ordering.Application.Common;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.Commands;

// Commands sent by the background workers. Actor is System by construction.

/// <summary>Draft TTL elapsed → expired. Sent by the expiry worker.</summary>
public sealed record ExpireOrderCommand(Guid OrderId) : ICommand<Result<OrderDto>>;

/// <summary>Restaurant never responded → system rejection (which starts the refund lifecycle).</summary>
public sealed record TimeoutOrderAcceptanceCommand(Guid OrderId) : ICommand<Result<OrderDto>>;

/// <summary>One refund attempt for a refund_pending order. Sent by the refund worker.</summary>
public sealed record ProcessRefundCommand(Guid OrderId) : ICommand<Result<OrderDto>>;

public sealed class ExpireOrderCommandValidator : AbstractValidator<ExpireOrderCommand>
{
    public ExpireOrderCommandValidator() => RuleFor(c => c.OrderId).NotEmpty();
}

public sealed class TimeoutOrderAcceptanceCommandValidator : AbstractValidator<TimeoutOrderAcceptanceCommand>
{
    public TimeoutOrderAcceptanceCommandValidator() => RuleFor(c => c.OrderId).NotEmpty();
}

public sealed class ProcessRefundCommandValidator : AbstractValidator<ProcessRefundCommand>
{
    public ProcessRefundCommandValidator() => RuleFor(c => c.OrderId).NotEmpty();
}

internal sealed class ExpireOrderCommandHandler(IOrderWriteRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ExpireOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(ExpireOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetForUpdateAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        var now = clock.GetUtcNow();
        if (order.Status == OrderStatus.Draft && now >= order.ExpiresAt)
        {
            order.TransitionTo(OrderStatus.Expired, Actor.System, now, "draft TTL elapsed");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
    }
}

internal sealed class TimeoutOrderAcceptanceCommandHandler(IOrderWriteRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<TimeoutOrderAcceptanceCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(TimeoutOrderAcceptanceCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetForUpdateAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        order.Reject(Actor.System, clock.GetUtcNow(), "restaurant did not respond within the acceptance timeout");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
    }
}

internal sealed class ProcessRefundCommandHandler(
    IOrderWriteRepository orders,
    IUnitOfWork unitOfWork,
    IRefundRail refundRail,
    IOptions<OrderingOptions> options,
    TimeProvider clock) : IRequestHandler<ProcessRefundCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(ProcessRefundCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetForUpdateAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        // Destination is the recorded payer, not a card charge id. No payer
        // means nothing to push to — leave the order untouched.
        if (order.Status != OrderStatus.RefundPending || string.IsNullOrWhiteSpace(order.PayerAddress))
        {
            return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
        }

        var now = clock.GetUtcNow();
        var refund = await refundRail.TransferAsync(order.PayerAddress, order.Total, cancellationToken);
        if (refund.Succeeded)
        {
            order.RecordRefundSuccess(refund.TxHash!, now);
        }
        else
        {
            var settings = options.Value.Refund;
            var backoff = RefundPolicy.Backoff(order.RefundAttempts + 1, settings.BackoffBaseMs, settings.BackoffCapMs);
            order.RecordRefundFailure(refund.Error ?? "refund transfer failed", now, settings.MaxAttempts, backoff);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
    }
}
