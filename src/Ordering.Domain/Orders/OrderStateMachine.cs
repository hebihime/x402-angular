namespace Ordering.Domain.Orders;

/// <summary>
/// The transition table is law, including the actor column. A transition is
/// valid only if the (from, to, actor) tuple exists here. A null From means
/// order creation.
/// </summary>
public static class OrderStateMachine
{
    public static readonly IReadOnlySet<(OrderStatus? From, OrderStatus To, Actor Actor)> Transitions =
        new HashSet<(OrderStatus?, OrderStatus, Actor)>
        {
            (null, OrderStatus.Draft, Actor.Customer),
            (OrderStatus.Draft, OrderStatus.Paid, Actor.System),
            (OrderStatus.Draft, OrderStatus.Cancelled, Actor.Customer),
            (OrderStatus.Draft, OrderStatus.Expired, Actor.System),
            (OrderStatus.Paid, OrderStatus.Accepted, Actor.Restaurant),
            (OrderStatus.Paid, OrderStatus.Rejected, Actor.Restaurant),
            (OrderStatus.Paid, OrderStatus.Rejected, Actor.System),
            (OrderStatus.Accepted, OrderStatus.Preparing, Actor.Restaurant),
            (OrderStatus.Preparing, OrderStatus.Ready, Actor.Restaurant),
            (OrderStatus.Ready, OrderStatus.Completed, Actor.Restaurant),
            (OrderStatus.Rejected, OrderStatus.RefundPending, Actor.System),
            (OrderStatus.RefundPending, OrderStatus.Refunded, Actor.System),
            (OrderStatus.RefundPending, OrderStatus.RefundFailed, Actor.System),
        };

    public static bool IsAllowed(OrderStatus? from, OrderStatus to, Actor actor) =>
        Transitions.Contains((from, to, actor));
}
