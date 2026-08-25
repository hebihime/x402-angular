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

    [Required]
    public X402Options X402 { get; set; } = new();

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

    public sealed class X402Options
    {
        [Required]
        public string PayToAddress { get; set; } = "";

        [Required]
        public string FacilitatorUrl { get; set; } = "https://x402.org/facilitator";

        [Required]
        public string Network { get; set; } = "base-sepolia";

        [Required]
        public string Asset { get; set; } = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";

        /// <summary>
        /// Demo/tests default to the deterministic fake. Set false to talk to
        /// <see cref="FacilitatorUrl"/> (optional testnet smoke).
        /// </summary>
        public bool UseFake { get; set; } = true;

        [Range(0, int.MaxValue)]
        public int FailNextVerifies { get; set; }

        [Range(0, int.MaxValue)]
        public int FailNextSettles { get; set; }
    }
}
