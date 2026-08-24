using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ordering.Domain;

/// <summary>
/// An amount of money in integer minor units (cents). The only representation
/// of money in the system; no decimal or floating-point type touches amounts.
/// Serialized as a string in JSON to avoid precision loss in clients.
/// </summary>
[JsonConverter(typeof(MoneyJsonConverter))]
public readonly record struct Money(long MinorUnits) : IComparable<Money>
{
    public static readonly Money Zero = new(0);

    public static Money operator +(Money a, Money b) => new(checked(a.MinorUnits + b.MinorUnits));

    public static Money operator *(Money a, int factor) => new(checked(a.MinorUnits * factor));

    public static bool operator >(Money a, Money b) => a.MinorUnits > b.MinorUnits;
    public static bool operator <(Money a, Money b) => a.MinorUnits < b.MinorUnits;
    public static bool operator >=(Money a, Money b) => a.MinorUnits >= b.MinorUnits;
    public static bool operator <=(Money a, Money b) => a.MinorUnits <= b.MinorUnits;

    public int CompareTo(Money other) => MinorUnits.CompareTo(other.MinorUnits);

    public override string ToString() => MinorUnits.ToString();
}

/// <summary>Money is always a string of minor units on the wire ("1250"), never a JSON number or decimal.</summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !long.TryParse(reader.GetString(), out var minorUnits))
        {
            throw new JsonException("Money must be a string of integer minor units.");
        }

        return new Money(minorUnits);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.MinorUnits.ToString());
}
