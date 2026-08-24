using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ordering.Domain.Orders;

[JsonConverter(typeof(SnakeCaseEnumConverter))]
public enum OrderStatus
{
    Draft,
    Paid,
    Cancelled,
    Expired,
    Accepted,
    Rejected,
    Preparing,
    Ready,
    Completed,
    RefundPending,
    Refunded,
    RefundFailed,
}

/// <summary>Enums are snake_case strings on the wire ("refund_pending"), matching <see cref="Wire"/>.</summary>
public sealed class SnakeCaseEnumConverter : JsonStringEnumConverter
{
    public SnakeCaseEnumConverter()
        : base(JsonNamingPolicy.SnakeCaseLower)
    {
    }
}
