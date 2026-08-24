using System.Text.Json;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// One serializer configuration for everything that persists or transports
/// JSON (order line snapshots, outbox payloads, projection columns). Money and
/// status converters come from attributes on the domain types, so amounts are
/// always strings of minor units and enums are snake_case everywhere.
/// </summary>
public static class OrderingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
