using System.Text.Json.Nodes;

namespace ConnectionTT.IpcContracts;

public sealed class IpcRequest
{
    public string Id { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public JsonObject? Args { get; set; }
}

public sealed class IpcResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public JsonNode? Result { get; set; }
    public string? Error { get; set; }
}
