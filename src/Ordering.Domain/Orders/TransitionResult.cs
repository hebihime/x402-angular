namespace Ordering.Domain.Orders;

/// <summary>
/// Outcome of an attempted transition. An invalid or repeated transition is
/// not an error: the order is left untouched, no event or history row is
/// produced, and the caller returns the current state.
/// </summary>
public readonly record struct TransitionResult(bool Transitioned)
{
    public static readonly TransitionResult Applied = new(true);
    public static readonly TransitionResult Ignored = new(false);
}
