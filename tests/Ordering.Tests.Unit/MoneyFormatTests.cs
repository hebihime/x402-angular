using FluentAssertions;
using Ordering.Mcp;

namespace Ordering.Tests.Unit;

public class MoneyFormatTests
{
    [Theory]
    [InlineData("1450", "$14.50")]
    [InlineData("0", "$0.00")]
    [InlineData("50", "$0.50")]
    [InlineData("1195", "$11.95")]
    [InlineData("100000", "$1,000.00")]
    [InlineData("-50", "-$0.50")]
    public void Formats_cents_as_usd_without_floats(string cents, string expected) =>
        MoneyFormat.Usd(cents).Should().Be(expected);

    [Fact]
    public void Survives_amounts_past_int32() =>
        MoneyFormat.Usd("100000000001").Should().Be("$1,000,000,000.01");

    [Theory]
    [InlineData("350", "+$3.50")]
    [InlineData("-50", "-$0.50")]
    [InlineData("0", "$0.00")]
    public void Signs_modifier_deltas(string cents, string expected) =>
        MoneyFormat.UsdDelta(cents).Should().Be(expected);

    [Fact]
    public void Rejects_non_integer_strings()
    {
        var act = () => MoneyFormat.Usd("14.50");
        act.Should().Throw<FormatException>();
    }
}
