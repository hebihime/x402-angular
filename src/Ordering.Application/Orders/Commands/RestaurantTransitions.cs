using FluentValidation;
using MediatR;
using Ordering.Application.Abstractions;
using Ordering.Application.Common;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.Commands;

// Dashboard transitions. The actor is Restaurant because these commands are
// only ever sent by the dashboard endpoints — the actor is a property of the
// command type, never of the request payload. Invalid or repeated transitions
// leave the order untouched and return its current state.

public interface IRestaurantTransitionCommand : ICommand<Result<OrderDto>>
{
    Guid RestaurantId { get; }
    Guid OrderId { get; }
}

public sealed record AcceptOrderCommand(Guid RestaurantId, Guid OrderId) : IRestaurantTransitionCommand;

public sealed record RejectOrderCommand(Guid RestaurantId, Guid OrderId, string? Reason) : IRestaurantTransitionCommand;

public sealed record StartPreparingCommand(Guid RestaurantId, Guid OrderId) : IRestaurantTransitionCommand;

public sealed record MarkReadyCommand(Guid RestaurantId, Guid OrderId) : IRestaurantTransitionCommand;

public sealed record CompleteOrderCommand(Guid RestaurantId, Guid OrderId) : IRestaurantTransitionCommand;

public sealed class RestaurantTransitionValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IRestaurantTransitionCommand
{
    public RestaurantTransitionValidator()
    {
        RuleFor(c => c.RestaurantId).NotEmpty();
        RuleFor(c => c.OrderId).NotEmpty();
    }
}

internal sealed class RestaurantTransitions(IOrderWriteRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<Result<OrderDto>> ApplyAsync(
        IRestaurantTransitionCommand command,
        Action<Order, DateTimeOffset> transition,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetForUpdateAsync(command.OrderId, cancellationToken);
        if (order is null || order.RestaurantId != command.RestaurantId)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        transition(order, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
    }
}

internal sealed class AcceptOrderCommandHandler(IOrderWriteRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<AcceptOrderCommand, Result<OrderDto>>
{
    public Task<Result<OrderDto>> Handle(AcceptOrderCommand command, CancellationToken cancellationToken) =>
        new RestaurantTransitions(orders, unitOfWork, clock).ApplyAsync(
            command,
            (order, now) => order.TransitionTo(OrderStatus.Accepted, Actor.Restaurant, now),
            cancellationToken);
}

internal sealed class RejectOrderCommandHandler(IOrderWriteRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<RejectOrderCommand, Result<OrderDto>>
{
    public Task<Result<OrderDto>> Handle(RejectOrderCommand command, CancellationToken cancellationToken) =>
        new RestaurantTransitions(orders, unitOfWork, clock).ApplyAsync(
            command,
            (order, now) => order.Reject(Actor.Restaurant, now, command.Reason),
            cancellationToken);
}

internal sealed class StartPreparingCommandHandler(IOrderWriteRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<StartPreparingCommand, Result<OrderDto>>
{
    public Task<Result<OrderDto>> Handle(StartPreparingCommand command, CancellationToken cancellationToken) =>
        new RestaurantTransitions(orders, unitOfWork, clock).ApplyAsync(
            command,
            (order, now) => order.TransitionTo(OrderStatus.Preparing, Actor.Restaurant, now),
            cancellationToken);
}

internal sealed class MarkReadyCommandHandler(IOrderWriteRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<MarkReadyCommand, Result<OrderDto>>
{
    public Task<Result<OrderDto>> Handle(MarkReadyCommand command, CancellationToken cancellationToken) =>
        new RestaurantTransitions(orders, unitOfWork, clock).ApplyAsync(
            command,
            (order, now) => order.TransitionTo(OrderStatus.Ready, Actor.Restaurant, now),
            cancellationToken);
}

internal sealed class CompleteOrderCommandHandler(IOrderWriteRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<CompleteOrderCommand, Result<OrderDto>>
{
    public Task<Result<OrderDto>> Handle(CompleteOrderCommand command, CancellationToken cancellationToken) =>
        new RestaurantTransitions(orders, unitOfWork, clock).ApplyAsync(
            command,
            (order, now) => order.TransitionTo(OrderStatus.Completed, Actor.Restaurant, now),
            cancellationToken);
}
