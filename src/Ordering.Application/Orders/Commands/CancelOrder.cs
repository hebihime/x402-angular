using FluentValidation;
using MediatR;
using Ordering.Application.Abstractions;
using Ordering.Application.Common;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.Commands;

/// <summary>Customer cancels a draft before payment. Any other state is left untouched and returned as-is.</summary>
public sealed record CancelOrderCommand(Guid OrderId, string CustomerId) : ICommand<Result<OrderDto>>;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.CustomerId).NotEmpty().MaximumLength(200);
    }
}

public sealed class CancelOrderCommandHandler(
    IOrderWriteRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<CancelOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetForUpdateAsync(command.OrderId, cancellationToken);
        if (order is null || order.CustomerId != command.CustomerId)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        order.TransitionTo(OrderStatus.Cancelled, Actor.Customer, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
    }
}
