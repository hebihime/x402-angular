namespace Ordering.Domain.Orders;

/// <summary>Exponential backoff with a cap for refund retries.</summary>
public static class RefundPolicy
{
    /// <param name="failedAttempts">Number of attempts that have already failed (1-based after the first failure).</param>
    public static TimeSpan Backoff(int failedAttempts, int baseMs, int capMs)
    {
        if (failedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttempts), "Backoff is computed after at least one failed attempt.");
        }

        // base * 2^(n-1), capped; exponent clamped to avoid overflow.
        var exponent = Math.Min(failedAttempts - 1, 30);
        var delayMs = Math.Min((long)baseMs << exponent, capMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }
}
