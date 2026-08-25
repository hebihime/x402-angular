using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Ordering.Mcp;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    // stdout is the MCP transport; every human-readable line must go to stderr.
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

var apiUrl = OptionalEnv("API_URL") ?? "http://localhost:5240";
var customerId = OptionalEnv("CUSTOMER_ID") ?? "mcp-agent";
var fakePayer = OptionalEnv("X402_FAKE_PAYER");
var agentKey = OptionalEnv("AGENT_PRIVATE_KEY");

var baseAddress = new Uri(apiUrl.TrimEnd('/') + "/");
var plain = new HttpClient { BaseAddress = baseAddress };

HttpClient paying = plain;
var canPay = false;
if (fakePayer is not null)
{
    paying = new HttpClient(new ChallengeRetryHandler(new FakePayerHeaderProvider(fakePayer), new HttpClientHandler()))
    {
        BaseAddress = baseAddress,
    };
    canPay = true;
}

builder.Services.AddSingleton(new ApiClient(plain, customerId, paying, canPay));
builder.Services.AddSingleton<OrderingTools>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "ordering", Version = "0.1.0" };
        options.ServerInstructions =
            "Order restaurant food and pay in USDC on Base Sepolia via x402. " +
            "Flow: list_restaurants -> get_menu -> place_order (free draft, server-priced) -> " +
            "confirm_order (pays the exact locked total) -> get_order_status. " +
            "The server is the only pricing authority: any price you send is ignored and the " +
            "draft total is locked at creation. Drafts expire, so confirm promptly. " +
            "Reuse the same idempotencyKey when retrying place_order; never re-draft a retry.";
    })
    .WithStdioServerTransport()
    .WithTools<OrderingMcpTools>();

var host = builder.Build();

Console.Error.WriteLine(
    $"ordering mcp server on stdio -> {baseAddress} customer={customerId} " +
    (canPay
        ? $"(fake payer {fakePayer})"
        : agentKey is not null
            ? "(AGENT_PRIVATE_KEY set but no C# x402 signer is wired; confirm_order will return the 402 unpaid)"
            : "(no wallet: confirm_order will return the 402 challenge unpaid)"));

await host.RunAsync();

static string? OptionalEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name)?.Trim();
    return string.IsNullOrEmpty(value) ? null : value;
}
