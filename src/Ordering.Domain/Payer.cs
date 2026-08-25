namespace Ordering.Domain;

/// <summary>Wallet identity at settlement. Stored and compared in lowercase.</summary>
public static class Payer
{
    public static string Normalize(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return address.Trim().ToLowerInvariant();
    }
}
