using FluentAssertions;
using Ordering.Domain;
using Ordering.Domain.Guardrails;
using Ordering.Domain.Orders;

namespace Ordering.Tests.Unit;

public class GuardrailAndRefundPolicyTests
{
    [Fact]
    public void Max_order_value_allows_at_the_cap_and_rejects_above_it()
    {
        SpendGuardrails.CheckMaxOrderValue(new Money(20000), new Money(20000)).Passed.Should().BeTrue();
        var over = SpendGuardrails.CheckMaxOrderValue(new Money(20001), new Money(20000));
        over.Passed.Should().BeFalse();
        over.Violation.Should().Contain("maximum order value");
    }

    [Fact]
    public void Daily_cap_considers_prior_spend_cumulatively()
    {
        SpendGuardrails.CheckDailySpendCap(new Money(40000), new Money(10000), new Money(50000)).Passed.Should().BeTrue();
        var over = SpendGuardrails.CheckDailySpendCap(new Money(40001), new Money(10000), new Money(50000));
        over.Passed.Should().BeFalse();
        over.Violation.Should().Contain("daily cap");
    }

    [Theory]
    [InlineData(1, 2000)]
    [InlineData(2, 4000)]
    [InlineData(3, 8000)]
    [InlineData(4, 16000)]
    [InlineData(5, 32000)]
    [InlineData(6, 60000)] // capped
    [InlineData(50, 60000)] // exponent clamp: no overflow far past the cap
    public void Refund_backoff_doubles_and_caps(int failedAttempts, int expectedMs)
    {
        RefundPolicy.Backoff(failedAttempts, baseMs: 2000, capMs: 60000)
            .Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void Refund_backoff_requires_at_least_one_failed_attempt()
    {
        var act = () => RefundPolicy.Backoff(0, 1000, 10000);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
