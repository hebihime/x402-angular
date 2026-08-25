using System.Text.Json.Nodes;

namespace Ordering.Mcp;

/// <summary>
/// A tool call that reached the API and got a non-2xx back. The API's own
/// error body is relayed untouched; nothing here reinterprets it.
/// HTTP 402 on confirm is not this — that challenge is returned as data.
/// </summary>
public sealed class ToolCallError : Exception
{
    public ToolCallError(int status, JsonNode? body)
        : base($"api responded {status}: {body}")
    {
        Status = status;
        Body = body;
    }

    public int Status { get; }

    public JsonNode? Body { get; }
}
