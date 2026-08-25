using System.Net.Http.Json;
using System.Text.Json;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Payments;

/// <summary>
/// Thin HTTP client of the x402 facilitator (<c>/verify</c>, <c>/settle</c>).
/// Does not settle after 2xx and does not invent replay semantics — the
/// confirm handler owns those.
/// </summary>
public sealed class HttpX402Facilitator(HttpClient http) : IFacilitator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FacilitatorVerifyResult> VerifyAsync(
        string paymentHeader,
        ExactPaymentRequirements requirements,
        CancellationToken cancellationToken)
    {
        if (!TryDecodePayload(paymentHeader, out var payload, out var reason))
        {
            return new FacilitatorVerifyResult.Invalid(reason);
        }

        var response = await http.PostAsJsonAsync(
            "verify",
            new FacilitatorRequest(X402Challenge.Version, payload, requirements),
            Json,
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<VerifyBody>(Json, cancellationToken);
        if (body is null)
        {
            return new FacilitatorVerifyResult.Invalid("facilitator_empty_response");
        }

        if (body.IsValid != true)
        {
            return new FacilitatorVerifyResult.Invalid(body.InvalidReason ?? "verification_failed");
        }

        var payer = body.Payer;
        return string.IsNullOrWhiteSpace(payer)
            ? new FacilitatorVerifyResult.Invalid("facilitator_returned_no_payer")
            : new FacilitatorVerifyResult.Valid(payer);
    }

    public async Task<FacilitatorSettleResult> SettleAsync(
        string paymentHeader,
        ExactPaymentRequirements requirements,
        CancellationToken cancellationToken)
    {
        if (!TryDecodePayload(paymentHeader, out var payload, out var reason))
        {
            return new FacilitatorSettleResult.Failed(reason);
        }

        var response = await http.PostAsJsonAsync(
            "settle",
            new FacilitatorRequest(X402Challenge.Version, payload, requirements),
            Json,
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SettleBody>(Json, cancellationToken);
        if (body is null)
        {
            return new FacilitatorSettleResult.Failed("facilitator_empty_response");
        }

        if (body.Success != true)
        {
            return new FacilitatorSettleResult.Failed(body.ErrorReason ?? "settlement_failed");
        }

        var payer = body.Payer;
        var txHash = body.Transaction;
        if (string.IsNullOrWhiteSpace(payer))
        {
            return new FacilitatorSettleResult.Failed("facilitator_returned_no_payer");
        }

        if (string.IsNullOrWhiteSpace(txHash))
        {
            return new FacilitatorSettleResult.Failed("facilitator_returned_no_tx");
        }

        return new FacilitatorSettleResult.Succeeded(payer, txHash);
    }

    private static bool TryDecodePayload(string paymentHeader, out JsonElement payload, out string reason)
    {
        payload = default;
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(paymentHeader));
            payload = JsonDocument.Parse(json).RootElement.Clone();
            reason = "";
            return true;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            return false;
        }
    }

    private sealed record FacilitatorRequest(int X402Version, JsonElement PaymentPayload, ExactPaymentRequirements PaymentRequirements);

    private sealed class VerifyBody
    {
        public bool? IsValid { get; set; }
        public string? Payer { get; set; }
        public string? InvalidReason { get; set; }
    }

    private sealed class SettleBody
    {
        public bool? Success { get; set; }
        public string? Payer { get; set; }
        public string? Transaction { get; set; }
        public string? ErrorReason { get; set; }
    }
}
