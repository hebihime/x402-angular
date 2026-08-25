namespace Ordering.Infrastructure.Payments;

public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string PayerAddress { get; set; } = "";
    public long AmountMinorUnits { get; set; }
    public string PaymentPayloadHash { get; set; } = "";
    public string TxHash { get; set; } = "";
    public DateTimeOffset SettledAt { get; set; }
}
