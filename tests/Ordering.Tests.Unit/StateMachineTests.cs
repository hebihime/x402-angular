using FluentAssertions;
using Ordering.Domain.Orders;

namespace Ordering.Tests.Unit;

public class StateMachineTests
{
    private static readonly OrderStatus?[] AllFroms =
        [null, .. Enum.GetValues<OrderStatus>().Cast<OrderStatus?>()];

    /// <summary>
    /// The transition table is law: exactly the 13 documented (from, to, actor)
    /// tuples are allowed and every other combination is rejected.
    /// </summary>
    [Fact]
    public void Exactly_the_documented_transitions_are_allowed()
    {
        var expected = new (OrderStatus? From, OrderStatus To, Actor Actor)[]
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

        OrderStateMachine.Transitions.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Every_tuple_outside_the_table_is_rejected_exhaustively()
    {
        var checkedTuples = 0;
        foreach (var from in AllFroms)
        {
            foreach (var to in Enum.GetValues<OrderStatus>())
            {
                foreach (var actor in Enum.GetValues<Actor>())
                {
                    var expected = OrderStateMachine.Transitions.Contains((from, to, actor));
                    OrderStateMachine.IsAllowed(from, to, actor).Should().Be(
                        expected,
                        "tuple ({0} -> {1}, {2}) must {3} allowed",
                        from?.ToString() ?? "(none)", to, actor, expected ? "be" : "not be");
                    checkedTuples++;
                }
            }
        }

        // 13 statuses-or-none x 12 statuses x 3 actors, of which only 13 are legal.
        checkedTuples.Should().Be(13 * 12 * 3);
        OrderStateMachine.Transitions.Should().HaveCount(13);
    }

    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Refunded)]
    public void Self_transitions_are_never_allowed(OrderStatus status)
    {
        foreach (var actor in Enum.GetValues<Actor>())
        {
            OrderStateMachine.IsAllowed(status, status, actor).Should().BeFalse();
        }
    }
}
