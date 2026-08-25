using System.Text.Json;
using FluentAssertions;
using Ordering.Domain;

namespace Ordering.Tests.Unit;

public class MoneyTests
{
    [Fact]
    public void Adds_and_multiplies_in_integer_minor_units()
    {
        (new Money(1195) + new Money(350)).Should().Be(new Money(1545));
        (new Money(1545) * 2).Should().Be(new Money(3090));
        Money.Zero.MinorUnits.Should().Be(0);
    }

    [Fact]
    public void Arithmetic_overflow_throws_instead_of_wrapping()
    {
        var nearMax = new Money(long.MaxValue - 1);
        var add = () => nearMax + new Money(2);
        var multiply = () => nearMax * 3;
        add.Should().Throw<OverflowException>();
        multiply.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Compares_by_amount()
    {
        (new Money(100) > new Money(99)).Should().BeTrue();
        (new Money(100) <= new Money(100)).Should().BeTrue();
        new Money(5).CompareTo(new Money(6)).Should().BeNegative();
    }

    [Fact]
    public void Serializes_as_a_string_of_minor_units_and_round_trips()
    {
        JsonSerializer.Serialize(new Money(1250)).Should().Be("\"1250\"");
        JsonSerializer.Deserialize<Money>("\"1250\"").Should().Be(new Money(1250));
    }

    [Theory]
    [InlineData("1250")]
    [InlineData("12.50")]
    [InlineData("null")]
    public void Rejects_non_string_or_non_integer_json(string json)
    {
        var act = () => JsonSerializer.Deserialize<Money>(json);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Converts_cents_to_USDC_atomic_units_at_the_protocol_edge()
    {
        Usdc.ToAtomicAmount(new Money(1450)).Should().Be("14500000");
        Usdc.ToAtomicAmount(Money.Zero).Should().Be("0");
    }
}
