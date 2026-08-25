namespace Ordering.Domain;

/// <summary>
/// Protocol-edge conversion between domain USD cents and USDC's 6-decimal
/// atomic units at 1:1. Domain money stays cents; never store USDC decimals.
/// </summary>
public static class Usdc
{
    public const int AtomicUnitsPerCent = 10_000;

    public static string ToAtomicAmount(Money cents) =>
        checked(cents.MinorUnits * AtomicUnitsPerCent).ToString();
}
