namespace Ordering.Mcp;

/// <summary>
/// Supplies an X-PAYMENT value that answers a 402 challenge. Returning null
/// leaves the 402 in place so confirm_order can relay it as data.
/// </summary>
public interface IPaymentHeaderProvider
{
    string? CreateHeader(HttpRequestMessage request, HttpResponseMessage challenge);
}
