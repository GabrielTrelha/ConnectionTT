using System.Text.Json;
using System.Text.Json.Nodes;
using ConnectionTT.Core;
using ConnectionTT.IpcContracts;

namespace ConnectionTT.Bridge;

public sealed class McpServer
{
    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "ConnectionTT";
    private const string ServerVersion = "1.0.0";

    private readonly AdvanceSteelPipeClient _client;

    public McpServer(AdvanceSteelPipeClient client) => _client = client;

    public async Task RunAsync(TextReader input, TextWriter output)
    {
        while (true)
        {
            string? line;
            try
            {
                line = await input.ReadLineAsync();
            }
            catch (Exception)
            {
                return;
            }

            if (line == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = await HandleAsync(line);
            if (response != null)
            {
                await output.WriteLineAsync(response);
                await output.FlushAsync();
            }
        }
    }

    private async Task<string?> HandleAsync(string line)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error");
        }

        if (message is not JsonObject obj)
        {
            return Error(null, -32600, "Invalid Request");
        }

        var method = obj["method"]?.GetValue<string>() ?? string.Empty;
        var hasId = obj["id"] != null;
        var id = obj["id"]?.DeepClone();

        switch (method)
        {
            case "initialize":
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = ServerName,
                        ["version"] = ServerVersion,
                    },
                });

            case "notifications/initialized":
            case "notifications/cancelled":
                return null;

            case "ping":
                return Result(id, new JsonObject());

            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = BuildToolList() });

            case "tools/call":
                return await HandleToolCall(id, obj);

            default:
                return hasId ? Error(id, -32601, $"Method not found: {method}") : null;
        }
    }

    private async Task<string> HandleToolCall(JsonNode? id, JsonObject message)
    {
        var name = message["params"]?["name"]?.GetValue<string>() ?? string.Empty;
        var argsNode = message["params"]?["arguments"];

        var args = argsNode is JsonObject argsObj ? argsObj : new JsonObject();

        string text;
        bool isError;

        try
        {
            var response = await _client.CallAsync(name, args);
            if (response.Ok)
            {
                text = response.Result?.ToJsonString(JsonDefaults.Options) ?? "{}";
                isError = false;
            }
            else
            {
                text = response.Error ?? "Erro desconhecido do Advance Steel.";
                isError = true;
            }
        }
        catch (Exception ex)
        {
            text = $"Advance Steel nao esta conectado: {ex.Message}";
            isError = true;
        }

        return Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = text,
            }),
            ["isError"] = isError,
        });
    }

    private static JsonArray BuildToolList()
    {
        var array = new JsonArray();
        foreach (var tool in ToolCatalog.All)
        {
            array.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema.DeepClone(),
            });
        }
        return array;
    }

    private static string Result(JsonNode? id, JsonNode result)
        => new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        }.ToJsonString(JsonDefaults.Options);

    private static string Error(JsonNode? id, int code, string message)
        => new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        }.ToJsonString(JsonDefaults.Options);
}
