using System.Globalization;
using System.Text;

namespace Ordering.Mcp;

/// <summary>
/// The MCP display edge (invariant 7). Integer minor-unit strings become
/// human-readable here and nowhere else; the integer string is always kept
/// alongside. Integer arithmetic only — no float, decimal, or double.
/// Matches the Angular money pipe: "$14.50", thousands separators, two cents.
/// </summary>
public static class MoneyFormat
{
    public static string Usd(string minorUnits)
    {
        if (!long.TryParse(minorUnits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cents))
        {
            throw new FormatException($"Money must be a string of integer minor units, got '{minorUnits}'.");
        }

        return Usd(cents);
    }

    public static string Usd(long cents)
    {
        var negative = cents < 0;
        var abs = negative ? checked(-cents) : cents;
        var whole = abs / 100;
        var frac = abs % 100;
        return $"{(negative ? "-" : "")}${Group(whole)}.{frac:D2}";
    }

    /// <summary>Signed variant for modifier price deltas ("+$1.50", "-$0.50").</summary>
    public static string UsdDelta(string minorUnits)
    {
        if (!long.TryParse(minorUnits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cents))
        {
            throw new FormatException($"Money must be a string of integer minor units, got '{minorUnits}'.");
        }

        var formatted = Usd(cents);
        return cents > 0 ? "+" + formatted : formatted;
    }

    private static string Group(long whole)
    {
        var digits = whole.ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= 3)
        {
            return digits;
        }

        var builder = new StringBuilder(digits.Length + digits.Length / 3);
        var remainder = digits.Length % 3;
        if (remainder > 0)
        {
            builder.Append(digits, 0, remainder);
        }

        for (var i = remainder; i < digits.Length; i += 3)
        {
            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(digits, i, 3);
        }

        return builder.ToString();
    }
}
