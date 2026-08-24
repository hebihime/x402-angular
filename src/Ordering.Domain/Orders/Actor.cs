using System.Text.Json.Serialization;

namespace Ordering.Domain.Orders;

/// <summary>
/// Who is performing a transition. Derived server-side from the surface that
/// was called (customer endpoints, dashboard endpoints, workers) — never from
/// request payloads.
/// </summary>
[JsonConverter(typeof(SnakeCaseEnumConverter))]
public enum Actor
{
    Customer,
    Restaurant,
    System,
}
