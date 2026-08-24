using FluentAssertions;
using Ordering.Domain;
using Ordering.Domain.Events;
using Ordering.Domain.Orders;

namespace Ordering.Tests.Unit;

public class OrderAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static Order PlaceDraft() => Order.Place(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "cust-1",
        "key-1",
        [new OrderLine(Guid.NewGuid(), "Pizza", new Money(1000), 2, [])],
        new Money(2000),
        T0,
        TimeSpan.FromMinutes(5));

    [Fact]
    public void Place_creates_a_draft_with_history_row_and_placed_event()
    {
        var order = PlaceDraft();

        order.Status.Should().Be(OrderStatus.Draft);
        order.Total.Should().Be(new Money(2000));
        order.ExpiresAt.Should().Be(T0 + TimeSpan.FromMinutes(5));
        order.History.Should().ContainSingle(h => h.From == null && h.To == OrderStatus.Draft && h.Actor == Actor.Customer);

        var events = order.DequeuePendingEvents();
        events.Should().ContainSingle().Which.Should().BeOfType<OrderPlaced>()
            .Which.Order.Status.Should().Be(OrderStatus.Draft);
        order.DequeuePendingEvents().Should().BeEmpty("events are drained exactly once");
    }

    [Fact]
    public void A_valid_transition_mutates_status_history_and_raises_one_event()
    {
        var order = PlaceDraft();
        order.DequeuePendingEvents();

        var result = order.TransitionTo(OrderStatus.Paid, Actor.System, T0.AddSeconds(10));

        result.Transitioned.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Paid);
        order.UpdatedAt.Should().Be(T0.AddSeconds(10));
        order.History.Should().HaveCount(2);
        order.DequeuePendingEvents().Should().ContainSingle()
            .Which.Should().BeOfType<OrderStatusChanged>()
            .Which.History.Should().Be(new HistoryDelta(OrderStatus.Draft, OrderStatus.Paid, Actor.System, T0.AddSeconds(10), null));
    }

    [Fact]
    public void Every_invalid_tuple_is_ignored_with_no_side_effects_exhaustively()
    {
        foreach (var to in Enum.GetValues<OrderStatus>())
        {
            foreach (var actor in Enum.GetValues<Actor>())
            {
                if (OrderStateMachine.IsAllowed(OrderStatus.Draft, to, actor))
                {
                    continue;
                }

                var order = PlaceDraft();
                order.DequeuePendingEvents();

                var result = order.TransitionTo(to, actor, T0.AddMinutes(1));

                result.Transitioned.Should().BeFalse("draft -> {0} by {1} is not in the table", to, actor);
                order.Status.Should().Be(OrderStatus.Draft);
                order.History.Should().HaveCount(1, "no history row for an ignored transition");
                order.DequeuePendingEvents().Should().BeEmpty("no event for an ignored transition");
            }
        }
    }

    [Fact]
    public void Repeated_transitions_are_idempotent()
    {
        var order = PlaceDraft();
        order.Confirm("ch_1", T0.AddSeconds(1));
        order.DequeuePendingEvents();

        var replay = order.Confirm("ch_2", T0.AddSeconds(2));

        replay.Transitioned.Should().BeFalse();
        order.ChargeId.Should().Be("ch_1", "a replayed confirm settles nothing");
        order.History.Should().HaveCount(2);
        order.DequeuePendingEvents().Should().BeEmpty();
    }

    [Fact]
    public void Reject_cascades_to_refund_pending_atomically()
    {
        var order = PlaceDraft();
        order.Confirm("ch_1", T0.AddSeconds(1));
        order.DequeuePendingEvents();

        var result = order.Reject(Actor.Restaurant, T0.AddMinutes(2), "out of stock");

        result.Transitioned.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.RefundPending);
        order.NextRefundAttemptAt.Should().Be(T0.AddMinutes(2), "the refund lifecycle starts immediately");
        order.History.Select(h => h.To).Should().ContainInOrder(
            OrderStatus.Draft, OrderStatus.Paid, OrderStatus.Rejected, OrderStatus.RefundPending);
        order.DequeuePendingEvents().Should().HaveCount(2, "rejected and refund_pending each get their own event");
    }

    [Fact]
    public void Refund_failures_back_off_then_exhaust_into_refund_failed_with_manual_flag()
    {
        var order = PlaceDraft();
        order.Confirm("ch_1", T0.AddSeconds(1));
        order.Reject(Actor.System, T0.AddMinutes(1));
        order.DequeuePendingEvents();

        var retry = order.RecordRefundFailure("gateway down", T0.AddMinutes(2), maxAttempts: 3, nextBackoff: TimeSpan.FromSeconds(4));
        retry.Transitioned.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.RefundPending);
        order.RefundAttempts.Should().Be(1);
        order.LastRefundError.Should().Be("gateway down");
        order.NextRefundAttemptAt.Should().Be(T0.AddMinutes(2) + TimeSpan.FromSeconds(4));
        order.DequeuePendingEvents().Should().ContainSingle().Which.Should().BeOfType<RefundAttemptFailed>()
            .Which.Attempt.Should().Be(1);

        order.RecordRefundFailure("gateway down", T0.AddMinutes(3), 3, TimeSpan.FromSeconds(8));
        var final = order.RecordRefundFailure("gateway down", T0.AddMinutes(4), 3, TimeSpan.FromSeconds(16));

        final.Transitioned.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.RefundFailed);
        order.RefundAttempts.Should().Be(3);
        order.ManualInterventionRequired.Should().BeTrue("the dashboard surfaces the manual-intervention flag");
        order.NextRefundAttemptAt.Should().BeNull("terminal state schedules nothing");

        // Terminal: nothing moves out of refund_failed, ever.
        order.RecordRefundFailure("again", T0.AddMinutes(5), 3, TimeSpan.Zero).Transitioned.Should().BeFalse();
        order.RecordRefundSuccess("re_1", T0.AddMinutes(6)).Transitioned.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.RefundFailed);
    }

    [Fact]
    public void Refund_success_records_the_refund_id()
    {
        var order = PlaceDraft();
        order.Confirm("ch_1", T0.AddSeconds(1));
        order.Reject(Actor.Restaurant, T0.AddMinutes(1));

        order.RecordRefundSuccess("re_1", T0.AddMinutes(2)).Transitioned.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Refunded);
        order.RefundId.Should().Be("re_1");
    }

    [Fact]
    public void The_total_never_changes_after_draft_creation()
    {
        var order = PlaceDraft();
        var locked = order.Total;

        order.Confirm("ch_1", T0.AddSeconds(1));
        order.Reject(Actor.Restaurant, T0.AddMinutes(1));
        order.RecordRefundFailure("x", T0.AddMinutes(2), 5, TimeSpan.FromSeconds(1));
        order.RecordRefundSuccess("re_1", T0.AddMinutes(3));

        order.Total.Should().Be(locked);
        order.Lines.Should().ContainSingle().Which.LineTotal.Should().Be(new Money(2000));
    }
}
