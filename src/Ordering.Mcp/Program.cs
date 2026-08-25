using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Ordering.Mcp;

if (args is ["demo"])
{
    Environment.ExitCode = await DemoRunner.RunAsync(CancellationToken.None);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    // stdout is the MCP transport; every human-readable line must go to stderr.
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

var apiUrl = McpRuntime.ApiUrl;
var customerId = McpRuntime.CustomerId;
var fakePayer = McpRuntime.FakePayer;
var agentKey = McpRuntime.AgentPrivateKey;
var canPay = !string.IsNullOrWhiteSpace(fakePayer);

builder.Services.AddSingleton(McpRuntime.CreateTools(customerId, fakePayer));
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
    $"ordering mcp server on stdio -> {McpRuntime.BaseAddress} customer={customerId} " +
    (canPay
        ? $"(fake payer {fakePayer})"
        : agentKey is not null
            ? "(AGENT_PRIVATE_KEY set but no C# x402 signer is wired; confirm_order will return the 402 unpaid)"
            : "(no wallet: confirm_order will return the 402 challenge unpaid)"));

await host.RunAsync();
