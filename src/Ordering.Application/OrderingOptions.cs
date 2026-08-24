using System.ComponentModel.DataAnnotations;

namespace Ordering.Application;

/// <summary>
/// All tunables, bound from the "Ordering" configuration section and validated
/// at startup — malformed or missing values fail boot.
/// </summary>
public sealed class OrderingOptions
{
    public const string SectionName = "Ordering";

    [Range(1, int.MaxValue)]
    public int DraftTtlSeconds { get; set; }

    [Range(1, int.MaxValue)]
    public int AcceptanceTimeoutSeconds { get; set; }

    [Range(1, long.MaxValue)]
    public long MaxOrderValueMinorUnits { get; set; }

    [Range(1, long.MaxValue)]
    public long DailySpendCapMinorUnits { get; set; }

    [Required]
    public RefundOptions Refund { get; set; } = new();

    [Required]
    public GatewayOptions Gateway { get; set; } = new();

    public sealed class RefundOptions
    {
        [Range(1, 100)]
        public int MaxAttempts { get; set; }

        [Range(1, int.MaxValue)]
        public int BackoffBaseMs { get; set; }

        [Range(1, int.MaxValue)]
        public int BackoffCapMs { get; set; }
    }

    /// <summary>Failure injection for the simulated gateway (demos and tests).</summary>
    public sealed class GatewayOptions
    {
        [Range(0, int.MaxValue)]
        public int FailNextCharges { get; set; }

        [Range(0, int.MaxValue)]
        public int FailNextRefunds { get; set; }
    }
}
