using System.Text.Json;

namespace Ordering.Domain.Orders;

/// <summary>
/// Snake_case wire names ("refund_pending", "restaurant") used consistently in
/// JSON, database columns, and the read model.
/// </summary>
public static class Wire
{
    private static readonly Dictionary<OrderStatus, string> StatusNames =
        Enum.GetValues<OrderStatus>().ToDictionary(s => s, s => JsonNamingPolicy.SnakeCaseLower.ConvertName(s.ToString()));

    private static readonly Dictionary<string, OrderStatus> StatusesByName =
        StatusNames.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly Dictionary<Actor, string> ActorNames =
        Enum.GetValues<Actor>().ToDictionary(a => a, a => JsonNamingPolicy.SnakeCaseLower.ConvertName(a.ToString()));

    private static readonly Dictionary<string, Actor> ActorsByName =
        ActorNames.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static string Name(this OrderStatus status) => StatusNames[status];
    public static string Name(this Actor actor) => ActorNames[actor];

    public static OrderStatus ParseOrderStatus(string wireName) => StatusesByName[wireName];
    public static Actor ParseActor(string wireName) => ActorsByName[wireName];

    public static bool TryParseOrderStatus(string wireName, out OrderStatus status) =>
        StatusesByName.TryGetValue(wireName, out status);
}
