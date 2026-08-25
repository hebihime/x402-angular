using System.Text;
using System.Text.Json;

namespace Ordering.Mcp;

/// <summary>
/// Answers a 402 with the fake facilitator's header format
/// (<c>{payer, nonce}</c> as base64 JSON). The nonce is the order id so a
/// replayed confirm of the same draft collides on payload/tx hash. Used by
/// tests, the demo, and <c>X402_FAKE_PAYER</c> — not a real wallet.
/// </summary>
public sealed class FakePayerHeaderProvider : IPaymentHeaderProvider
{
    public FakePayerHeaderProvider(string payer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payer);
        Payer = payer;
    }

    public string Payer { get; }

    public string? CreateHeader(HttpRequestMessage request, HttpResponseMessage challenge)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // /api/orders/{orderId}/confirm
        if (segments.Length < 4
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("orders", StringComparison.OrdinalIgnoreCase)
            || !segments[3].Equals("confirm", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[2], out var orderId))
        {
            return null;
        }

        var json = JsonSerializer.Serialize(new { payer = Payer, nonce = orderId.ToString("N") });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
