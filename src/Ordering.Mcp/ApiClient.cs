using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ordering.Mcp;

/// <summary>
/// The MCP server's entire relationship with the core API: one HTTP call per
/// tool. No caching, no retries, no interpretation of what came back.
///
/// Two clients, because exactly one endpoint behaves differently: the paying
/// client answers the 402 challenge and retries with X-PAYMENT. Everything
/// else uses the plain client.
/// </summary>
public sealed class ApiClient
{
    public const string CustomerIdHeader = "X-Customer-Id";
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly HttpClient _http;
    private readonly HttpClient _paying;

    public ApiClient(HttpClient http, string customerId, HttpClient? paying = null, bool canPay = false)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        _http = http;
        _paying = paying ?? http;
        CustomerId = customerId;
        CanPay = canPay && paying is not null;
    }

    public string CustomerId { get; }

    /// <summary>
    /// True when a wallet (or fake paying seam) is configured; confirm_order
    /// says so in its output rather than guessing what the agent should do.
    /// </summary>
    public bool CanPay { get; }

    public Task<ApiResponse> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, content: null, extraHeaders: null, paying: false, cancellationToken);

    public Task<ApiResponse> PostAsync(
        string path,
        HttpContent? content,
        IReadOnlyDictionary<string, string>? extraHeaders,
        bool paying,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, content, extraHeaders, paying, cancellationToken);

    private async Task<ApiResponse> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        IReadOnlyDictionary<string, string>? extraHeaders,
        bool paying,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, TrimPath(path)) { Content = content };
        request.Headers.TryAddWithoutValidation(CustomerIdHeader, CustomerId);
        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = paying ? _paying : _http;
        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? body;
        try
        {
            body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            body = new JsonObject
            {
                ["error"] = "non_json_response",
                ["message"] = text,
            };
        }

        var status = (int)response.StatusCode;
        return new ApiResponse(status, status is >= 200 and < 300, body);
    }

    private static string TrimPath(string path) => path.TrimStart('/');
}

public sealed record ApiResponse(int Status, bool Ok, JsonNode? Body);
