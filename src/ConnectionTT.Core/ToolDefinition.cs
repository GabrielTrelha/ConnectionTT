using System.Text.Json.Nodes;

namespace ConnectionTT.Core;

public sealed class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonObject InputSchema { get; set; } = new();
}
