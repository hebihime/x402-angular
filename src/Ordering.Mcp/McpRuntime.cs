namespace Ordering.Mcp;

/// <summary>Shared env + HttpClient wiring for the stdio server and the demo.</summary>
internal static class McpRuntime
{
    public static string ApiUrl => OptionalEnv("API_URL") ?? "http://localhost:5240";

    public static string CustomerId => OptionalEnv("CUSTOMER_ID") ?? "mcp-agent";

    public static string? FakePayer => OptionalEnv("X402_FAKE_PAYER");

    public static string? AgentPrivateKey => OptionalEnv("AGENT_PRIVATE_KEY");

    public static Uri BaseAddress => new(ApiUrl.TrimEnd('/') + "/");

    public static OrderingTools CreateTools(string customerId, string? fakePayer)
    {
        var baseAddress = BaseAddress;
        var plain = new HttpClient { BaseAddress = baseAddress };
        HttpClient paying = plain;
        var canPay = false;
        if (!string.IsNullOrWhiteSpace(fakePayer))
        {
            paying = new HttpClient(
                new ChallengeRetryHandler(new FakePayerHeaderProvider(fakePayer), new HttpClientHandler()))
            {
                BaseAddress = baseAddress,
            };
            canPay = true;
        }

        return new OrderingTools(new ApiClient(plain, customerId, paying, canPay));
    }

    public static string? OptionalEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
