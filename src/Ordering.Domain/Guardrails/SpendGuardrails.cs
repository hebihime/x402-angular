namespace Ordering.Domain.Guardrails;

public sealed record GuardrailCheck(bool Passed, string? Violation)
{
    public static readonly GuardrailCheck Ok = new(true, null);
    public static GuardrailCheck Fail(string violation) => new(false, violation);
}

/// <summary>
/// Spend guardrails. Max-order runs at draft and again at confirm. Daily cap
/// at draft keys on <c>X-Customer-Id</c>; at confirm it keys on the verified
/// payer wallet.
/// </summary>
public static class SpendGuardrails
{
    public static GuardrailCheck CheckMaxOrderValue(Money orderTotal, Money maxOrderValue)
    {
        if (orderTotal > maxOrderValue)
        {
            return GuardrailCheck.Fail(
                $"Order total {orderTotal.MinorUnits} exceeds the maximum order value of {maxOrderValue.MinorUnits} minor units.");
        }

        return GuardrailCheck.Ok;
    }

    public static GuardrailCheck CheckDailySpendCap(Money priorSpendToday, Money orderTotal, Money dailyCap)
    {
        if (priorSpendToday + orderTotal > dailyCap)
        {
            return GuardrailCheck.Fail(
                $"Order total {orderTotal.MinorUnits} plus prior spend today {priorSpendToday.MinorUnits} exceeds the daily cap of {dailyCap.MinorUnits} minor units.");
        }

        return GuardrailCheck.Ok;
    }
}
